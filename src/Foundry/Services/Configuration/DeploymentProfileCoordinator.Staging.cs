// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.AccessControl;
using System.Security.Principal;

namespace Foundry.Services.Configuration;

public sealed partial class DeploymentProfileCoordinator
{
    private const string StagingLeaseFileName = ".lease";
    private readonly List<StagingLease> stagingDirectories = [];
    private static string StagingRoot => Path.Combine(Constants.DeploymentProfilesDirectoryPath, "Staging");

    private string CreateStagingDirectory()
    {
        using FileStream rootLease = AcquireStagingRootLease();
        CleanupAbandonedStagingDirectoriesCore();
        string directory = Path.Combine(StagingRoot, Guid.NewGuid().ToString("N"));
        CreatePrivateDirectory(directory);
        FileStream lease = OpenStagingLease(directory);
        stagingDirectories.Add(new StagingLease(directory, lease));
        return directory;
    }

    private void CleanupAbandonedStagingDirectories()
    {
        try
        {
            using FileStream rootLease = AcquireStagingRootLease();
            CleanupAbandonedStagingDirectoriesCore();
        }
        catch (Exception exception) when (IsStagingCleanupFailure(exception)) { }
    }

    private static void CleanupAbandonedStagingDirectoriesCore()
    {
        foreach (string directory in Directory.EnumerateDirectories(StagingRoot).Take(1024))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) continue;
            try
            {
                ValidateStagingDirectory(directory);
                using FileStream lease = OpenStagingLease(directory);
                DeleteStagingContents(directory);
                lease.Dispose();
                File.Delete(Path.Combine(directory, StagingLeaseFileName));
                Directory.Delete(directory, false);
            }
            catch (Exception exception) when (IsStagingCleanupFailure(exception)) { }
        }
    }

    private void ClearStagingDirectories(bool releaseFailedLeases = false)
    {
        try
        {
            using FileStream rootLease = AcquireStagingRootLease();
            foreach (StagingLease entry in stagingDirectories.ToArray())
            {
                try
                {
                    if (Directory.Exists(entry.Path))
                    {
                        ValidateStagingDirectory(entry.Path);
                        entry.Lease ??= OpenStagingLease(entry.Path);
                        DeleteStagingContents(entry.Path);
                        entry.Lease.Dispose();
                        entry.Lease = null;
                        File.Delete(Path.Combine(entry.Path, StagingLeaseFileName));
                        Directory.Delete(entry.Path, false);
                    }
                    entry.Lease?.Dispose();
                    stagingDirectories.Remove(entry);
                }
                catch (Exception exception) when (IsStagingCleanupFailure(exception)) { }
            }
        }
        catch (Exception exception) when (IsStagingCleanupFailure(exception)) { }
        finally
        {
            if (releaseFailedLeases)
            {
                foreach (StagingLease entry in stagingDirectories)
                {
                    entry.Lease?.Dispose();
                    entry.Lease = null;
                }
            }
        }
    }

    private static FileStream AcquireStagingRootLease()
    {
        ValidateNoReparseAncestors(StagingRoot);
        CreatePrivateDirectory(StagingRoot);
        string path = Path.Combine(StagingRoot, ".cleanup-lock");
        ValidateNoReparseAncestors(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static FileStream OpenStagingLease(string directory)
    {
        ValidateStagingDirectory(directory);
        string path = Path.Combine(directory, StagingLeaseFileName);
        ValidateNoReparseAncestors(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static void DeleteStagingContents(string directory)
    {
        ValidateStagingDirectory(directory);
        // Materialized profile assets are flat. Never descend into an unexpected directory or link.
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            if (Path.GetFileName(path) == StagingLeaseFileName) continue;
            ValidateNoReparseAncestors(path);
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                throw new IOException("A profile staging directory contains an unexpected subdirectory.");
            File.Delete(path);
        }
    }

    private static void ValidateStagingDirectory(string directory)
    {
        string fullPath = Path.GetFullPath(directory);
        if (!string.Equals(Path.GetDirectoryName(fullPath), Path.GetFullPath(StagingRoot), StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(fullPath), "N", out _))
            throw new IOException("The profile staging path is outside its owned directory.");
        ValidateNoReparseAncestors(fullPath);
    }

    private static void ValidateNoReparseAncestors(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Profile staging cannot use a redirected path.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void CreatePrivateDirectory(string path)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(
            identity.User ?? throw new InvalidOperationException("The current Windows user is unavailable."),
            FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        var directory = new DirectoryInfo(path);
        directory.Create(security);
        directory.SetAccessControl(security);
    }

    private static bool IsStagingCleanupFailure(Exception exception) => exception is IOException or UnauthorizedAccessException;

    private sealed class StagingLease(string path, FileStream lease)
    {
        public string Path { get; } = path;
        public FileStream? Lease { get; set; } = lease;
    }
}
