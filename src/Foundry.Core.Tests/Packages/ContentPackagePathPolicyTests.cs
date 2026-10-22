// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.Core.Services.Packages;

namespace Foundry.Core.Tests.Packages;

public sealed class ContentPackagePathPolicyTests
{
    [Fact]
    public void ResolveLexically_FutureDestinationDoesNotInspectReparsePoints()
    {
        using var junction = new TemporaryJunction();

        string resolved = ContentPackagePathPolicy.PostInstallation.ResolveLexically(junction.Link, "package/script.ps1");

        Assert.Equal(Path.Combine(junction.Link, "package", "script.ps1"), resolved);
        Assert.False(Directory.Exists(Path.Combine(junction.Target, "package")));
    }

    [Fact]
    public void Resolve_ActualDestinationRejectsReparsePoints()
    {
        using var junction = new TemporaryJunction();

        Assert.Throws<InvalidDataException>(() => ContentPackagePathPolicy.PostInstallation.Resolve(junction.Link, "package/script.ps1"));
    }

    [Fact]
    public void ResolveLexically_NormalizesFutureRoot()
    {
        string resolved = ContentPackagePathPolicy.PostInstallation.ResolveLexically(@"C:\Future\unused\..\Payloads\", "package/script.ps1");

        Assert.Equal(@"C:\Future\Payloads\package\script.ps1", resolved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../script.ps1")]
    [InlineData("package/../../script.ps1")]
    [InlineData("C:/script.ps1")]
    [InlineData("/script.ps1")]
    [InlineData("script.ps1:secret")]
    [InlineData("CON.txt")]
    [InlineData("LPT1.exe")]
    [InlineData("a./script.ps1")]
    [InlineData("a /script.ps1")]
    [InlineData("a\\script.ps1")]
    public void ResolveLexically_UnsafeRelativePathIsRejected(string path)
    {
        Assert.Throws<InvalidDataException>(() => ContentPackagePathPolicy.PostInstallation.ResolveLexically(@"C:\Future\Payloads", path));
    }

    [Fact]
    public void ResolveLexically_RelativePathBeyondLimitIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => ContentPackagePathPolicy.PostInstallation.ResolveLexically(@"C:\Future", new string('a', 111)));
    }

    [Fact]
    public void ValidateRelativePath_PostInstallationAcceptsExactlyTheLimit()
    {
        ContentPackagePathPolicy.PostInstallation.ValidateRelativePath(new string('a', 110));
    }

    [Fact]
    public void ValidateRelativePath_UsesTheLimitOfItsKind()
    {
        var longerKind = ContentPackageKind.PostInstallation with { MaximumRelativePathLength = 150 };

        new ContentPackagePathPolicy(longerKind).ValidateRelativePath(new string('a', 111));
        Assert.Throws<InvalidDataException>(() => ContentPackagePathPolicy.PostInstallation.ValidateRelativePath(new string('a', 111)));
    }

    [Theory]
    [InlineData(245, true)]
    [InlineData(246, false)]
    public void ResolveLexically_FullPathLengthBoundaryIsEnforced(int rootNameLength, bool accepted)
    {
        string root = @"C:\" + new string('a', rootNameLength);
        if (accepted)
        {
            string resolved = ContentPackagePathPolicy.PostInstallation.ResolveLexically(root, "script.ps1");
            Assert.Equal(root + @"\script.ps1", resolved);
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => ContentPackagePathPolicy.PostInstallation.ResolveLexically(root, "script.ps1"));
        }
    }

    private sealed class TemporaryJunction : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "FoundryPathPolicyTests", Guid.NewGuid().ToString("N"));

        public TemporaryJunction()
        {
            Link = Path.Combine(_root, "link");
            Target = Path.Combine(_root, "target");
            Directory.CreateDirectory(Target);
            try
            {
                var startInfo = new ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                foreach (string argument in new[] { "/c", "mklink", "/J", Link, Target }) startInfo.ArgumentList.Add(argument);
                using var process = Process.Start(startInfo)!;
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                Assert.True(process.ExitCode == 0, $"Junction creation failed: {output} {error}");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public string Link { get; }
        public string Target { get; }

        public void Dispose()
        {
            if (Directory.Exists(Link)) Directory.Delete(Link);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void DriversKindAccepts150AndRejects151CharacterRelativePaths()
    {
        var policy = new ContentPackagePathPolicy(ContentPackageKind.Drivers);
        string accepted = "d/" + new string('a', 144) + ".inf";
        Assert.Equal(150, accepted.Length);

        policy.ValidateRelativePath(accepted);

        Assert.Throws<InvalidDataException>(() => policy.ValidateRelativePath("d/" + new string('a', 145) + ".inf"));
    }
}
