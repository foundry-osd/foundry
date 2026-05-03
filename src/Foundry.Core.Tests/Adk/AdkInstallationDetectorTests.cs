using Foundry.Core.Services.Adk;

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

    [Fact]
    public void Detect_WhenStrictAdkVersionIsMissing_UsesComponentVersionFallback()
    {
        FakeAdkInstallationProbe probe = CreateInstalledProbe(null);
        probe.Products =
        [
            new("Windows Deployment Tools", "10.1.26100.2454"),
            new("Windows PE x64 x86 Add-ons", "10.1.26100.2454")
        ];

        AdkInstallationStatus status = new AdkInstallationDetector(probe).Detect();

        Assert.Equal("10.1.26100.2454", status.InstalledVersion);
        Assert.True(status.IsCompatible);
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
            ]
        };

        if (!string.IsNullOrWhiteSpace(adkVersion))
        {
            probe.Products = [new("Windows Assessment and Deployment Kit", adkVersion)];
        }

        return probe;
    }

    private sealed class FakeAdkInstallationProbe : IAdkInstallationProbe
    {
        public string? KitsRootPath { get; init; }
        public IReadOnlyCollection<string> ExistingDirectories { get; init; } = [];
        public IReadOnlyList<AdkInstalledProduct> Products { get; set; } = [];

        public string? GetKitsRootPath() => KitsRootPath;

        public bool DirectoryExists(string path)
        {
            return ExistingDirectories.Contains(path, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<AdkInstalledProduct> GetInstalledProducts() => Products;
    }
}
