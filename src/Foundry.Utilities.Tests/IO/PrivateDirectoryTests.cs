// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Foundry.Utilities.IO;

namespace Foundry.Utilities.Tests.IO;

public sealed class PrivateDirectoryTests
{
    [Fact]
    public void CreateOwnerOnly_GrantsOnlyCurrentUser()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string path = Path.Combine(temporaryDirectory.Path, "private", "nested");

        PrivateDirectory.CreateOwnerOnly(path);

        AssertOwnerOnly(path);
    }

    [Fact]
    public void CreateOwnerOnly_ExistingDirectory_ReplacesAcl()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string path = Path.Combine(temporaryDirectory.Path, "existing");
        Directory.CreateDirectory(path);

        PrivateDirectory.CreateOwnerOnly(path);

        AssertOwnerOnly(path);
    }

    [Fact]
    public void CreateLeased_ThenCleanupAbandoned_KeepsLeasedDirectory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string root = Path.Combine(temporaryDirectory.Path, "root");
        string leasedPath;
        using (PrivateDirectoryLease lease = PrivateDirectory.CreateLeased(root))
        {
            leasedPath = lease.Path;
            string nested = Path.Combine(lease.Path, "nested", "input.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
            File.WriteAllText(nested, "leased");

            PrivateDirectoryCleanupResult result = PrivateDirectory.CleanupAbandoned(root);

            Assert.Equal(new PrivateDirectoryCleanupResult(0, 0), result);
            Assert.Equal("leased", File.ReadAllText(nested));
            AssertOwnerOnly(lease.Path);
        }

        Assert.False(Directory.Exists(leasedPath));
    }

    [Fact]
    public void CleanupAbandoned_RemovesUnleasedDirectories()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string root = temporaryDirectory.Path;
        string abandoned = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(abandoned, "nested"));
        File.WriteAllText(Path.Combine(abandoned, "nested", "input.txt"), "abandoned");
        File.WriteAllText(Path.Combine(abandoned, ".lease"), string.Empty);
        string unrelated = Path.Combine(root, "unrelated");
        Directory.CreateDirectory(unrelated);

        PrivateDirectoryCleanupResult result = PrivateDirectory.CleanupAbandoned(root);

        Assert.Equal(new PrivateDirectoryCleanupResult(1, 0), result);
        Assert.False(Directory.Exists(abandoned));
        Assert.True(Directory.Exists(unrelated));
    }

    [Fact]
    public void CleanupAbandoned_LockedFileInside_CountsFailureAndKeepsDirectory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string root = temporaryDirectory.Path;
        using PrivateDirectoryLease live = PrivateDirectory.CreateLeased(root);
        string abandoned = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(abandoned);
        string locked = Path.Combine(abandoned, "input.pfx");
        using (new FileStream(locked, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            PrivateDirectoryCleanupResult result = PrivateDirectory.CleanupAbandoned(root);
            using PrivateDirectoryLease next = PrivateDirectory.CreateLeased(root);

            Assert.Equal(new PrivateDirectoryCleanupResult(0, 1), result);
            Assert.Equal(new PrivateDirectoryCleanupResult(0, 1), next.AbandonedCleanup);
            Assert.True(File.Exists(locked));
        }

        Assert.Equal(new PrivateDirectoryCleanupResult(1, 0), PrivateDirectory.CleanupAbandoned(root));
        Assert.True(Directory.Exists(live.Path));
    }

    [Fact]
    public void CleanupAbandoned_TooManyEntries_CountsFailureAndKeepsDirectory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string root = temporaryDirectory.Path;
        string abandoned = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(abandoned);
        foreach (string name in new[] { "a.txt", "b.txt", "c.txt" }) File.WriteAllText(Path.Combine(abandoned, name), name);

        PrivateDirectoryCleanupResult result = PrivateDirectory.CleanupAbandoned(root, maximumEntries: 2);

        Assert.Equal(new PrivateDirectoryCleanupResult(0, 1), result);
        Assert.True(Directory.Exists(abandoned));
    }

    [Fact]
    public void ThrowIfRedirected_JunctionInside_Throws()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string root = Path.Combine(temporaryDirectory.Path, "root");
        string outside = Path.Combine(temporaryDirectory.Path, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        string link = Path.Combine(root, "link");
        if (!TryCreateRedirection(link, outside, out string failure)) Assert.Skip($"Junction creation is not available here: {failure}");

        try
        {
            PrivateDirectory.ThrowIfRedirected(Path.Combine(root, "plain", "input.txt"), root);
            Assert.Throws<IOException>(() => PrivateDirectory.ThrowIfRedirected(Path.Combine(link, "input.txt"), root));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public void DeleteContents_RemovesNestedFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string root = temporaryDirectory.Path;
        using PrivateDirectoryLease lease = PrivateDirectory.CreateLeased(root);
        Directory.CreateDirectory(Path.Combine(lease.Path, "a", "b"));
        File.WriteAllText(Path.Combine(lease.Path, "a", "b", "deep.txt"), "deep");
        File.WriteAllText(Path.Combine(lease.Path, "top.txt"), "top");

        lease.DeleteContents();

        Assert.Equal([Path.Combine(lease.Path, ".lease")], Directory.EnumerateFileSystemEntries(lease.Path));
        Assert.Equal(new PrivateDirectoryCleanupResult(0, 0), PrivateDirectory.CleanupAbandoned(root));
    }

    private static void AssertOwnerOnly(string path)
    {
        DirectorySecurity security = new DirectoryInfo(path).GetAccessControl();
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        FileSystemAccessRule[] rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();

        Assert.True(security.AreAccessRulesProtected);
        FileSystemAccessRule rule = Assert.Single(rules);
        Assert.Equal(identity.User!.Value, rule.IdentityReference.Value);
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights & FileSystemRights.FullControl);
        Assert.False(rule.IsInherited);
        Assert.Equal(InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, rule.InheritanceFlags);
    }

    private static bool TryCreateRedirection(string link, string target, out string failure)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            failure = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Symbolic links need a privilege the unelevated session lacks; a junction does not.
        }

        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "/c", "mklink", "/J", link, target }) startInfo.ArgumentList.Add(argument);
        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        failure = $"{output} {error}".Trim();
        return process.ExitCode == 0;
    }
}
