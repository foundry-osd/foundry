using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeMountedImageAssetProvisioningServiceTests
{
    [Fact]
    public async Task ProvisionAsync_WritesBootstrapStartnetAndCurl()
    {
        using TempMountedImage image = TempMountedImage.Create();
        string curlSourcePath = Path.Combine(image.RootPath, "tools", "curl.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(curlSourcePath)!);
        File.WriteAllText(curlSourcePath, "curl");
        string startnetPath = Path.Combine(image.MountedImagePath, "Windows", "System32", "startnet.cmd");
        File.WriteAllLines(startnetPath, ["wpeinit", "echo existing"]);

        var service = new WinPeMountedImageAssetProvisioningService();

        WinPeResult result = await service.ProvisionAsync(
            new WinPeMountedImageAssetProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                Architecture = WinPeArchitecture.X64,
                BootstrapScriptContent = "Write-Host 'Foundry'",
                CurlExecutableSourcePath = curlSourcePath,
                IanaWindowsTimeZoneMapJson = "{}"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal("Write-Host 'Foundry'", await File.ReadAllTextAsync(Path.Combine(image.System32Path, "FoundryBootstrap.ps1")));
        Assert.Equal("curl", await File.ReadAllTextAsync(Path.Combine(image.System32Path, "curl.exe")));

        string[] startnetLines = await File.ReadAllLinesAsync(startnetPath);
        Assert.Contains(startnetLines, line => line.Equals("wpeinit", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(startnetLines, line => line.Equals("echo existing", StringComparison.OrdinalIgnoreCase));
        Assert.Single(startnetLines, line => line.Contains("FoundryBootstrap.ps1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProvisionAsync_WhenRunTwice_DoesNotDuplicateBootstrapInvocation()
    {
        using TempMountedImage image = TempMountedImage.Create();
        string curlSourcePath = Path.Combine(image.RootPath, "curl.exe");
        File.WriteAllText(curlSourcePath, "curl");

        var service = new WinPeMountedImageAssetProvisioningService();
        var options = new WinPeMountedImageAssetProvisioningOptions
        {
            MountedImagePath = image.MountedImagePath,
            Architecture = WinPeArchitecture.X64,
            BootstrapScriptContent = "bootstrap",
            CurlExecutableSourcePath = curlSourcePath,
            IanaWindowsTimeZoneMapJson = "{}"
        };

        WinPeResult firstResult = await service.ProvisionAsync(options, CancellationToken.None);
        WinPeResult secondResult = await service.ProvisionAsync(options, CancellationToken.None);

        Assert.True(firstResult.IsSuccess, firstResult.Error?.Details);
        Assert.True(secondResult.IsSuccess, secondResult.Error?.Details);

        string[] startnetLines = await File.ReadAllLinesAsync(Path.Combine(image.System32Path, "startnet.cmd"));
        Assert.Single(startnetLines, line => line.Contains("FoundryBootstrap.ps1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProvisionAsync_WritesConfigurationAssetsAndSourceMarkers()
    {
        using TempMountedImage image = TempMountedImage.Create();
        string curlSourcePath = Path.Combine(image.RootPath, "curl.exe");
        File.WriteAllText(curlSourcePath, "curl");
        string assetSourcePath = Path.Combine(image.RootPath, "profile.xml");
        File.WriteAllText(assetSourcePath, "<WLANProfile />");

        var service = new WinPeMountedImageAssetProvisioningService();

        WinPeResult result = await service.ProvisionAsync(
            new WinPeMountedImageAssetProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                Architecture = WinPeArchitecture.X64,
                BootstrapScriptContent = "bootstrap",
                CurlExecutableSourcePath = curlSourcePath,
                IanaWindowsTimeZoneMapJson = "{\"zones\":[]}",
                FoundryConnectConfigurationJson = "{\"schemaVersion\":1}",
                ExpertDeployConfigurationJson = "{\"schemaVersion\":2}",
                FoundryConnectAssetFiles =
                [
                    new FoundryConnectProvisionedAssetFile
                    {
                        SourcePath = assetSourcePath,
                        RelativeDestinationPath = @"Foundry\Config\Network\Wifi\Profiles\profile.xml"
                    }
                ],
                AutopilotProfiles =
                [
                    new AutopilotProfileSettings
                    {
                        Id = "profile-1",
                        DisplayName = "Profile 1",
                        FolderName = "Profile1",
                        Source = "test",
                        ImportedAtUtc = DateTimeOffset.UnixEpoch,
                        JsonContent = "{\"profile\":1}"
                    }
                ],
                ConnectProvisioningSource = WinPeProvisioningSource.Local,
                DeployProvisioningSource = WinPeProvisioningSource.Release
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.True(Directory.Exists(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Network", "Wired", "Profiles")));
        Assert.Equal("{\"schemaVersion\":1}", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "foundry.connect.config.json")));
        Assert.Equal("{\"schemaVersion\":2}", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "foundry.deploy.config.json")));
        Assert.Equal("local", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "foundry.connect.provisioning-source.txt")));
        Assert.Equal("release", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "foundry.deploy.provisioning-source.txt")));
        Assert.Equal("{\"zones\":[]}", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "iana-windows-timezones.json")));
        Assert.Equal("<WLANProfile />", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Network", "Wifi", "Profiles", "profile.xml")));
        Assert.Equal("{\"profile\":1}", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Autopilot", "Profile1", "AutopilotConfigurationFile.json")));
    }

    [Fact]
    public async Task ProvisionAsync_CreatesRuntimeLogAndTempDirectories()
    {
        using TempMountedImage image = TempMountedImage.Create();
        string curlSourcePath = Path.Combine(image.RootPath, "curl.exe");
        File.WriteAllText(curlSourcePath, "curl");

        var service = new WinPeMountedImageAssetProvisioningService();

        WinPeResult result = await service.ProvisionAsync(
            new WinPeMountedImageAssetProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                Architecture = WinPeArchitecture.X64,
                BootstrapScriptContent = "bootstrap",
                CurlExecutableSourcePath = curlSourcePath,
                IanaWindowsTimeZoneMapJson = "{}"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.True(Directory.Exists(Path.Combine(image.MountedImagePath, "Foundry", "Logs")));
        Assert.True(Directory.Exists(Path.Combine(image.MountedImagePath, "Foundry", "Temp")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenDeployConfigurationIsMissing_WritesCompleteDefaultDeployConfiguration()
    {
        using TempMountedImage image = TempMountedImage.Create();
        string curlSourcePath = Path.Combine(image.RootPath, "curl.exe");
        File.WriteAllText(curlSourcePath, "curl");

        var service = new WinPeMountedImageAssetProvisioningService();

        WinPeResult result = await service.ProvisionAsync(
            new WinPeMountedImageAssetProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                Architecture = WinPeArchitecture.X64,
                BootstrapScriptContent = "bootstrap",
                CurlExecutableSourcePath = curlSourcePath,
                IanaWindowsTimeZoneMapJson = "{}"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        string deployConfigurationJson = await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "foundry.deploy.config.json"));
        Assert.Contains("\"schemaVersion\": 2", deployConfigurationJson, StringComparison.Ordinal);
        Assert.Contains("\"localization\":", deployConfigurationJson, StringComparison.Ordinal);
        Assert.Contains("\"customization\":", deployConfigurationJson, StringComparison.Ordinal);
        Assert.Contains("\"autopilot\":", deployConfigurationJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisionAsync_WhenConnectConfigurationIsMissing_WritesCompleteDefaultConnectConfiguration()
    {
        using TempMountedImage image = TempMountedImage.Create();
        string curlSourcePath = Path.Combine(image.RootPath, "curl.exe");
        File.WriteAllText(curlSourcePath, "curl");

        var service = new WinPeMountedImageAssetProvisioningService();

        WinPeResult result = await service.ProvisionAsync(
            new WinPeMountedImageAssetProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                Architecture = WinPeArchitecture.X64,
                BootstrapScriptContent = "bootstrap",
                CurlExecutableSourcePath = curlSourcePath,
                IanaWindowsTimeZoneMapJson = "{}"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        string connectConfigurationJson = await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "foundry.connect.config.json"));
        using JsonDocument document = JsonDocument.Parse(connectConfigurationJson);
        JsonElement root = document.RootElement;
        Assert.True(root.TryGetProperty("schemaVersion", out _));
        Assert.True(root.TryGetProperty("capabilities", out _));
        Assert.True(root.TryGetProperty("dot1x", out _));
        Assert.True(root.TryGetProperty("wifi", out _));
        Assert.True(root.TryGetProperty("internetProbe", out _));
        Assert.False(root.TryGetProperty("network", out _));
    }

    [Fact]
    public async Task ProvisionAsync_WhenMediaSecretKeyIsProvided_WritesSecretKeyUnderConfigSecrets()
    {
        using TempMountedImage image = TempMountedImage.Create();
        string curlSourcePath = Path.Combine(image.RootPath, "curl.exe");
        File.WriteAllText(curlSourcePath, "curl");
        byte[] secretKey = [1, 2, 3, 4];

        var service = new WinPeMountedImageAssetProvisioningService();

        WinPeResult result = await service.ProvisionAsync(
            new WinPeMountedImageAssetProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                Architecture = WinPeArchitecture.X64,
                BootstrapScriptContent = "bootstrap",
                CurlExecutableSourcePath = curlSourcePath,
                IanaWindowsTimeZoneMapJson = "{}",
                MediaSecretsKey = secretKey
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(secretKey, await File.ReadAllBytesAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Secrets", "media-secrets.key")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenMediaSecretKeyIsMissing_DoesNotCreateSecretsDirectory()
    {
        using TempMountedImage image = TempMountedImage.Create();
        string curlSourcePath = Path.Combine(image.RootPath, "curl.exe");
        File.WriteAllText(curlSourcePath, "curl");

        var service = new WinPeMountedImageAssetProvisioningService();

        WinPeResult result = await service.ProvisionAsync(
            new WinPeMountedImageAssetProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                Architecture = WinPeArchitecture.X64,
                BootstrapScriptContent = "bootstrap",
                CurlExecutableSourcePath = curlSourcePath,
                IanaWindowsTimeZoneMapJson = "{}"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.False(Directory.Exists(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Secrets")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenSevenZipSourceIsProvided_CopiesRuntimeTools()
    {
        using TempMountedImage image = TempMountedImage.Create();
        string curlSourcePath = Path.Combine(image.RootPath, "curl.exe");
        File.WriteAllText(curlSourcePath, "curl");
        string sevenZipSourcePath = Path.Combine(image.RootPath, "7z");
        Directory.CreateDirectory(Path.Combine(sevenZipSourcePath, "x64"));
        File.WriteAllText(Path.Combine(sevenZipSourcePath, "x64", "7za.exe"), "7za");
        File.WriteAllText(Path.Combine(sevenZipSourcePath, "License.txt"), "license");
        File.WriteAllText(Path.Combine(sevenZipSourcePath, "readme.txt"), "readme");

        var service = new WinPeMountedImageAssetProvisioningService();

        WinPeResult result = await service.ProvisionAsync(
            new WinPeMountedImageAssetProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                Architecture = WinPeArchitecture.X64,
                BootstrapScriptContent = "bootstrap",
                CurlExecutableSourcePath = curlSourcePath,
                SevenZipSourceDirectoryPath = sevenZipSourcePath,
                IanaWindowsTimeZoneMapJson = "{}"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        string toolsPath = Path.Combine(image.MountedImagePath, "Foundry", "Tools", "7zip");
        Assert.Equal("7za", await File.ReadAllTextAsync(Path.Combine(toolsPath, "x64", "7za.exe")));
        Assert.Equal("license", await File.ReadAllTextAsync(Path.Combine(toolsPath, "License.txt")));
        Assert.Equal("readme", await File.ReadAllTextAsync(Path.Combine(toolsPath, "readme.txt")));
    }

    private sealed class TempMountedImage : IDisposable
    {
        private TempMountedImage(string rootPath, string mountedImagePath)
        {
            RootPath = rootPath;
            MountedImagePath = mountedImagePath;
            System32Path = Path.Combine(mountedImagePath, "Windows", "System32");
        }

        public string RootPath { get; }
        public string MountedImagePath { get; }
        public string System32Path { get; }

        public static TempMountedImage Create()
        {
            string rootPath = Path.Combine(Path.GetTempPath(), $"foundry-asset-provisioning-{Guid.NewGuid():N}");
            string mountedImagePath = Path.Combine(rootPath, "mount");
            Directory.CreateDirectory(Path.Combine(mountedImagePath, "Windows", "System32"));
            return new TempMountedImage(rootPath, mountedImagePath);
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
