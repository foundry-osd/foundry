// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Adk;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.Adk;

public sealed class AdkInstallationDetectorTests
{
    [Fact]
    public void Detect_WhenRequiredFoldersAreMissing_ReturnsNotInstalled()
    {
        FakeAdkInstallationProbe probe = new()
        {
            KitsRootPath = @"C:\Program Files (x86)\Windows Kits\10\"
        };

        AdkInstallationStatus status = new AdkInstallationDetector(probe).Detect();

        Assert.False(status.IsInstalled);
        Assert.False(status.IsCompatible);
        Assert.False(status.IsWinPeAddonInstalled);
        Assert.False(status.CanCreateMedia);
    }

    [Fact]
    public void Detect_WhenDeploymentToolsAndWinPeAddonArePresent_ReturnsInstalled()
    {
        FakeAdkInstallationProbe probe = CreateInstalledProbe("10.1.26100.2454");

        AdkInstallationStatus status = new AdkInstallationDetector(probe).Detect();

        Assert.True(status.IsInstalled);
        Assert.True(status.IsWinPeAddonInstalled);
        Assert.Equal("10.1.26100.2454", status.InstalledVersion);
    }

    [Fact]
    public void Detect_WhenDeploymentToolsArePresentAndWinPeAddonIsMissing_ReturnsAdkInstalledButMediaBlocked()
    {
        string kitsRootPath = @"C:\Program Files (x86)\Windows Kits\10\";
        FakeAdkInstallationProbe probe = new()
        {
            KitsRootPath = kitsRootPath,
            ExistingDirectories =
            [
                Path.Combine(kitsRootPath, AdkInstallationDetector.DeploymentToolsRelativePath)
            ],
            Products = [new("Windows Assessment and Deployment Kit", "10.1.26100.2454")]
        };

        AdkInstallationStatus status = new AdkInstallationDetector(probe).Detect();

        Assert.True(status.IsInstalled);
        Assert.True(status.IsCompatible);
        Assert.False(status.IsWinPeAddonInstalled);
        Assert.False(status.CanCreateMedia);
    }

    [Fact]
    public void Detect_WhenWinPeAddonFolderExistsButRequiredToolsAreMissing_BlocksMediaCreation()
    {
        string kitsRootPath = @"C:\Program Files (x86)\Windows Kits\10\";
        FakeAdkInstallationProbe probe = new()
        {
            KitsRootPath = kitsRootPath,
            ExistingDirectories =
            [
                Path.Combine(kitsRootPath, AdkInstallationDetector.DeploymentToolsRelativePath),
                Path.Combine(kitsRootPath, AdkInstallationDetector.WinPeRelativePath)
            ],
            Products = [new("Windows Assessment and Deployment Kit", "10.1.26100.2454")]
        };

        AdkInstallationStatus status = new AdkInstallationDetector(probe).Detect();

        Assert.True(status.IsInstalled);
        Assert.True(status.IsCompatible);
        Assert.False(status.IsWinPeAddonInstalled);
        Assert.False(status.CanCreateMedia);
    }

    [Theory]
    [InlineData("10.1.26100.1", false)]
    [InlineData("10.1.26100.2453", false)]
    [InlineData("10.1.26100.2454", true)]
    [InlineData("10.1.26100.3000", true)]
    [InlineData("10.1.28000.1", false)]
    [InlineData("10.1.28000.2", false)]
    [InlineData("10.1.22621.1", false)]
    [InlineData("11.0.26100.1", false)]
    public void Detect_EvaluatesCompatibilityFromSupportedAdkBuildLines(string installedVersion, bool expectedCompatible)
    {
        FakeAdkInstallationProbe probe = CreateInstalledProbe(installedVersion);

        AdkInstallationStatus status = new AdkInstallationDetector(probe).Detect();

        Assert.Equal(expectedCompatible, status.IsCompatible);
        Assert.Equal(expectedCompatible, status.CanCreateMedia);
    }

    [Theory]
    [InlineData("10.1.22621.1", AdkVersionRelation.BelowSupported)]
    [InlineData("10.1.26100.1", AdkVersionRelation.BelowSupported)]
    [InlineData("10.1.26100.2453", AdkVersionRelation.BelowSupported)]
    [InlineData("10.1.28000.1", AdkVersionRelation.AboveSupported)]
    [InlineData("bad-version", AdkVersionRelation.Unknown)]
    public void Detect_ClassifiesInstalledVersionAgainstSupportedBuildLine(
        string installedVersion,
        AdkVersionRelation expectedRelation)
    {
        FakeAdkInstallationProbe probe = CreateInstalledProbe(installedVersion);

        AdkInstallationStatus status = new AdkInstallationDetector(probe).Detect();

        Assert.Equal(expectedRelation, status.VersionRelation);
    }

    [Fact]
    public void Detect_WhenStrictAdkVersionIsMissing_UsesComponentVersionFallback()
    {
        FakeAdkInstallationProbe probe = CreateInstalledProbe(null);
        probe.Products =
        [
            new("Windows Deployment Tools", "10.1.26100.2454"),
            new("Windows PE wims (DesktopEditions)", "10.1.26100.2454")
        ];

        AdkInstallationStatus status = new AdkInstallationDetector(probe).Detect();

        Assert.Equal("10.1.26100.2454", status.InstalledVersion);
        Assert.True(status.IsCompatible);
    }

    [Fact]
    public void Detect_WhenOnlyWinPeVersionIsKnown_DoesNotInferAdkCompatibility()
    {
        var probe = CreateInstalledProbe(null);
        probe.Products = [new("Windows PE wims (DesktopEditions)", "10.1.26100.2454")];

        Assert.False(new AdkInstallationDetector(probe).Detect().CanCreateMedia);
    }

    [Theory]
    [InlineData("10.1.22621.1")]
    [InlineData("10.1.26100.1")]
    [InlineData("10.1.28000.1")]
    [InlineData(null)]
    public void Detect_WhenWinPeVersionDoesNotMatch_BlocksMedia(string? version)
    {
        var probe = CreateInstalledProbe("10.1.26100.2454");
        probe.Products = probe.Products.Select(p => p.DisplayName.StartsWith("Windows PE", StringComparison.Ordinal)
            ? p with { DisplayVersion = version } : p).ToArray();

        AdkInstallationStatus status = new AdkInstallationDetector(probe).Detect();

        Assert.True(status.IsWinPeAddonInstalled);
        Assert.True(status.IsWinPeAddonRegistered);
        Assert.False(status.IsWinPeAddonCompatible);
        Assert.False(status.CanCreateMedia);
    }

    [Fact]
    public void Detect_WhenRegisteredWinPeScriptsAreMissing_RequiresRepairInsteadOfNewInstallation()
    {
        var probe = CreateInstalledProbe("10.1.26100.2454");
        probe.ExistingFiles = probe.ExistingFiles.Where(path => !path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)).ToArray();

        AdkInstallationStatus status = new AdkInstallationDetector(probe).Detect();

        Assert.True(status.IsWinPeAddonRegistered);
        Assert.False(status.IsWinPeAddonInstalled);
        Assert.False(status.CanCreateMedia);
    }

    [Theory]
    [InlineData("amd64", true, @"en-us\winpe.wim")]
    [InlineData("arm64", true, @"WinPE_OCs\WinPE-SecureStartup.cab")]
    [InlineData("amd64", true, @"WinPE_OCs\WinPE-SecureStartup.cab")]
    [InlineData("arm64", true, @"Media\Boot\boot.sdi")]
    [InlineData("amd64", true, @"Media\EFI\Microsoft\Boot\BCD")]
    [InlineData("arm64", false, @"Oscdimg\efisys.bin")]
    [InlineData("amd64", false, @"Oscdimg\efisys_noprompt.bin")]
    [InlineData("arm64", false, @"Oscdimg\efisys_EX.bin")]
    [InlineData("amd64", false, @"Oscdimg\efisys_noprompt_EX.bin")]
    public void Detect_WhenRequiredAssetIsMissing_OnlyBlocksAffectedArchitecture(string architecture, bool winPeAsset, string relativePath)
    {
        var probe = CreateInstalledProbe("10.1.26100.2454");
        string missing = Path.Combine(probe.KitsRootPath!, winPeAsset
            ? AdkInstallationDetector.WinPeRelativePath : AdkInstallationDetector.DeploymentToolsRelativePath, architecture, relativePath);
        Assert.Contains(missing, probe.ExistingFiles);
        probe.ExistingFiles = probe.ExistingFiles.Where(p => p != missing).ToArray();

        AdkInstallationStatus status = new AdkInstallationDetector(probe).Detect();

        Assert.True(status.CanCreateMedia);
        Assert.Equal(architecture != "amd64", status.CanCreateMediaFor(WinPeArchitecture.X64));
        Assert.Equal(architecture != "arm64", status.CanCreateMediaFor(WinPeArchitecture.Arm64));
        Assert.False(status.CanCreateMediaFor((WinPeArchitecture)99));
    }

    private static FakeAdkInstallationProbe CreateInstalledProbe(string? adkVersion)
    {
        string kitsRootPath = @"C:\Program Files (x86)\Windows Kits\10\";
        FakeAdkInstallationProbe probe = new()
        {
            KitsRootPath = kitsRootPath,
            ExistingDirectories =
            [
                Path.Combine(kitsRootPath, AdkInstallationDetector.DeploymentToolsRelativePath),
                Path.Combine(kitsRootPath, AdkInstallationDetector.WinPeRelativePath)
            ],
            ExistingFiles =
            [
                Path.Combine(kitsRootPath, AdkInstallationDetector.WinPeRelativePath, "copype.cmd"),
                Path.Combine(kitsRootPath, AdkInstallationDetector.WinPeRelativePath, "MakeWinPEMedia.cmd")
            ]
        };

        if (!string.IsNullOrWhiteSpace(adkVersion))
        {
            probe.Products =
            [
                new("Windows Assessment and Deployment Kit", adkVersion),
                new("Windows PE wims (DesktopEditions)", adkVersion),
                new("Windows PE Optional Packages (DesktopEditions)", adkVersion),
                new("Windows PE Scripts", adkVersion),
                new("Windows PE Boot Files (DesktopEditions)", adkVersion)
            ];
        }

        foreach (string architecture in new[] { "amd64", "arm64" })
        {
            probe.ExistingFiles = probe.ExistingFiles.Concat(new[]
            {
                Path.Combine(kitsRootPath, AdkInstallationDetector.WinPeRelativePath, architecture, "en-us", "winpe.wim"),
                Path.Combine(kitsRootPath, AdkInstallationDetector.WinPeRelativePath, architecture, "WinPE_OCs", "WinPE-SecureStartup.cab"),
                Path.Combine(kitsRootPath, AdkInstallationDetector.WinPeRelativePath, architecture, "Media", "Boot", "boot.sdi"),
                Path.Combine(kitsRootPath, AdkInstallationDetector.WinPeRelativePath, architecture, "Media", "EFI", "Microsoft", "Boot", "BCD"),
                Path.Combine(kitsRootPath, AdkInstallationDetector.DeploymentToolsRelativePath, architecture, "Oscdimg", "efisys.bin"),
                Path.Combine(kitsRootPath, AdkInstallationDetector.DeploymentToolsRelativePath, architecture, "Oscdimg", "efisys_noprompt.bin"),
                Path.Combine(kitsRootPath, AdkInstallationDetector.DeploymentToolsRelativePath, architecture, "Oscdimg", "efisys_EX.bin"),
                Path.Combine(kitsRootPath, AdkInstallationDetector.DeploymentToolsRelativePath, architecture, "Oscdimg", "efisys_noprompt_EX.bin")
            }).ToArray();
        }

        return probe;
    }

    private sealed class FakeAdkInstallationProbe : IAdkInstallationProbe
    {
        public string? KitsRootPath { get; init; }
        public IReadOnlyCollection<string> ExistingDirectories { get; init; } = [];
        public IReadOnlyCollection<string> ExistingFiles { get; set; } = [];
        public IReadOnlyList<AdkInstalledProduct> Products { get; set; } = [];

        public string? GetKitsRootPath() => KitsRootPath;

        public bool DirectoryExists(string path)
        {
            return ExistingDirectories.Contains(path, StringComparer.OrdinalIgnoreCase);
        }

        public bool FileExists(string path)
        {
            return ExistingFiles.Contains(path, StringComparer.OrdinalIgnoreCase);
        }

        public bool DirectoryContainsFile(string directoryPath, string fileName)
        {
            return ExistingFiles.Any(path =>
                path.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<AdkInstalledProduct> GetInstalledProducts() => Products;
    }
}
