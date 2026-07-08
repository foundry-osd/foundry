// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Xml.Linq;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeMountedImageAssetProvisioningServiceTests
{
    [Fact]
    public async Task ProvisionAsync_WritesBootstrapPSBootstrapperCurlAndStartnet()
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
                PSBootstrapperSourceExecutablePath = image.PSBootstrapperSourcePath,
                IanaWindowsTimeZoneMapJson = "{}"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal("Write-Host 'Foundry'", await File.ReadAllTextAsync(Path.Combine(image.System32Path, "FoundryBootstrap.ps1")));
        Assert.Equal("curl", await File.ReadAllTextAsync(Path.Combine(image.System32Path, "curl.exe")));
        Assert.Equal("psbootstrapper", await File.ReadAllTextAsync(Path.Combine(image.System32Path, "psbootstrapper.exe")));

        // startnet.cmd must contain only wpeinit (plus any unrelated pre-existing lines); the
        // bootstrap is launched from X:\Unattend.xml, not startnet.
        string[] startnetLines = await File.ReadAllLinesAsync(startnetPath);
        Assert.Contains(startnetLines, line => line.Equals("wpeinit", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(startnetLines, line => line.Equals("echo existing", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(startnetLines, line => line.Contains("FoundryBootstrap.ps1", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(startnetLines, line => line.Contains("psbootstrapper.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProvisionAsync_WritesUnattendAtImageRootWithLaunchCommands()
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
                PSBootstrapperSourceExecutablePath = image.PSBootstrapperSourcePath,
                IanaWindowsTimeZoneMapJson = "{}",
                IncludeTroubleshootingConsole = true
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);

        string unattendPath = Path.Combine(image.MountedImagePath, "Unattend.xml");
        Assert.True(File.Exists(unattendPath));

        XDocument document = XDocument.Load(unattendPath);
        XNamespace ns = "urn:schemas-microsoft-com:unattend";
        XElement component = document.Descendants(ns + "component").Single();
        Assert.Equal("Microsoft-Windows-Setup", (string?)component.Attribute("name"));
        Assert.Equal("amd64", (string?)component.Attribute("processorArchitecture"));

        XElement settings = document.Descendants(ns + "settings").Single();
        Assert.Equal("windowsPE", (string?)settings.Attribute("pass"));

        Assert.Equal("true", component.Element(ns + "EnableFirewall")?.Value);
        Assert.Equal("true", component.Element(ns + "EnableNetwork")?.Value);
        XElement display = component.Element(ns + "Display")!;
        Assert.Equal("32", display.Element(ns + "ColorDepth")?.Value);
        Assert.Equal("1280", display.Element(ns + "HorizontalResolution")?.Value);
        Assert.Equal("720", display.Element(ns + "VerticalResolution")?.Value);
        Assert.Equal("60", display.Element(ns + "RefreshRate")?.Value);

        string syncPath = component.Descendants(ns + "RunSynchronousCommand").Single().Element(ns + "Path")!.Value;
        Assert.Contains("psbootstrapper.exe", syncPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FoundryBootstrap.ps1", syncPath, StringComparison.OrdinalIgnoreCase);

        string asyncPath = component.Descendants(ns + "RunAsynchronousCommand").Single().Element(ns + "Path")!.Value;
        Assert.Contains("powershell.exe", asyncPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-NoExit", asyncPath, StringComparison.Ordinal);
        Assert.Contains("-WindowStyle Minimized", asyncPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisionAsync_ByDefault_OmitsTroubleshootingConsoleFromUnattend()
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
                PSBootstrapperSourceExecutablePath = image.PSBootstrapperSourcePath,
                IanaWindowsTimeZoneMapJson = "{}"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);

        XDocument document = XDocument.Load(Path.Combine(image.MountedImagePath, "Unattend.xml"));
        XNamespace ns = "urn:schemas-microsoft-com:unattend";
        Assert.Empty(document.Descendants(ns + "RunAsynchronousCommand"));
        Assert.Single(document.Descendants(ns + "RunSynchronousCommand"));
    }

    [Fact]
    public async Task ProvisionAsync_WhenFirewallDisabled_WritesEnableFirewallFalse()
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
                PSBootstrapperSourceExecutablePath = image.PSBootstrapperSourcePath,
                IanaWindowsTimeZoneMapJson = "{}",
                EnableFirewall = false
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        XDocument document = XDocument.Load(Path.Combine(image.MountedImagePath, "Unattend.xml"));
        XNamespace ns = "urn:schemas-microsoft-com:unattend";
        Assert.Equal("false", document.Descendants(ns + "EnableFirewall").Single().Value);
    }

    [Fact]
    public async Task ProvisionAsync_WhenArm64_WritesArm64UnattendArchitecture()
    {
        using TempMountedImage image = TempMountedImage.Create();
        string curlSourcePath = Path.Combine(image.RootPath, "curl.exe");
        File.WriteAllText(curlSourcePath, "curl");

        var service = new WinPeMountedImageAssetProvisioningService();

        WinPeResult result = await service.ProvisionAsync(
            new WinPeMountedImageAssetProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                Architecture = WinPeArchitecture.Arm64,
                BootstrapScriptContent = "bootstrap",
                CurlExecutableSourcePath = curlSourcePath,
                PSBootstrapperSourceExecutablePath = image.PSBootstrapperSourcePath,
                IanaWindowsTimeZoneMapJson = "{}"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        XDocument document = XDocument.Load(Path.Combine(image.MountedImagePath, "Unattend.xml"));
        XNamespace ns = "urn:schemas-microsoft-com:unattend";
        Assert.Equal("arm64", (string?)document.Descendants(ns + "component").Single().Attribute("processorArchitecture"));
    }

    [Fact]
    public async Task ProvisionAsync_WhenPSBootstrapperSourceIsMissing_ReturnsFailure()
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
                PSBootstrapperSourceExecutablePath = Path.Combine(image.RootPath, "missing", "psbootstrapper.exe"),
                IanaWindowsTimeZoneMapJson = "{}"
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("psbootstrapper.exe", result.Error?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProvisionAsync_WhenRunTwice_KeepsStartnetToWpeinitOnly()
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
            PSBootstrapperSourceExecutablePath = image.PSBootstrapperSourcePath,
            IanaWindowsTimeZoneMapJson = "{}"
        };

        WinPeResult firstResult = await service.ProvisionAsync(options, CancellationToken.None);
        WinPeResult secondResult = await service.ProvisionAsync(options, CancellationToken.None);

        Assert.True(firstResult.IsSuccess, firstResult.Error?.Details);
        Assert.True(secondResult.IsSuccess, secondResult.Error?.Details);

        string[] startnetLines = await File.ReadAllLinesAsync(Path.Combine(image.System32Path, "startnet.cmd"));
        Assert.Single(startnetLines, line => line.Equals("wpeinit", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(startnetLines, line => line.Contains("FoundryBootstrap.ps1", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(startnetLines, line => line.Contains("psbootstrapper.exe", StringComparison.OrdinalIgnoreCase));
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
                PSBootstrapperSourceExecutablePath = image.PSBootstrapperSourcePath,
                IanaWindowsTimeZoneMapJson = "{\"zones\":[]}",
                FoundryConnectConfigurationJson = "{\"schemaVersion\":1}",
                DeployConfigurationJson = "{\"schemaVersion\":2}",
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
                ConnectProvisioningSource = WinPeProvisioningSource.Debug,
                DeployProvisioningSource = WinPeProvisioningSource.Release
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal("{\"schemaVersion\":1}", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "foundry.connect.config.json")));
        Assert.Equal("{\"schemaVersion\":2}", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "foundry.deploy.config.json")));
        Assert.Equal("debug", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "foundry.connect.provisioning-source.txt")));
        Assert.Equal("release", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "foundry.deploy.provisioning-source.txt")));
        Assert.Equal("{\"zones\":[]}", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "iana-windows-timezones.json")));
        Assert.Equal("<WLANProfile />", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Network", "Wifi", "Profiles", "profile.xml")));
        Assert.False(Directory.Exists(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Network", "Wired")));
        Assert.False(Directory.Exists(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Network", "Certificates")));
        Assert.Equal("{\"profile\":1}", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Autopilot", "Profile1", "AutopilotConfigurationFile.json")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenJsonProfileMode_WritesAutopilotProfileAssetsOnly()
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
                PSBootstrapperSourceExecutablePath = image.PSBootstrapperSourcePath,
                IanaWindowsTimeZoneMapJson = "{}",
                AutopilotProvisioningMode = AutopilotProvisioningMode.JsonProfile,
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
                ]
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.True(File.Exists(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Autopilot", "Profile1", "AutopilotConfigurationFile.json")));
        Assert.False(Directory.Exists(Path.Combine(image.MountedImagePath, "Foundry", "Tools", "OA3")));
        Assert.False(Directory.Exists(Path.Combine(image.MountedImagePath, "Foundry", "Runtime", "AutopilotHash")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenHardwareHashMode_WritesHashAssetsOnly()
    {
        using TempMountedImage image = TempMountedImage.Create();
        string curlSourcePath = Path.Combine(image.RootPath, "curl.exe");
        string oa3SourcePath = Path.Combine(image.RootPath, "oa3tool.exe");
        File.WriteAllText(curlSourcePath, "curl");
        File.WriteAllText(oa3SourcePath, "oa3");

        var service = new WinPeMountedImageAssetProvisioningService();

        WinPeResult result = await service.ProvisionAsync(
            new WinPeMountedImageAssetProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                Architecture = WinPeArchitecture.X64,
                BootstrapScriptContent = "bootstrap",
                CurlExecutableSourcePath = curlSourcePath,
                PSBootstrapperSourceExecutablePath = image.PSBootstrapperSourcePath,
                IanaWindowsTimeZoneMapJson = "{}",
                AutopilotProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
                Oa3ToolSourcePath = oa3SourcePath,
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
                ]
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal("oa3", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Tools", "OA3", "oa3tool.exe")));
        string oa3ConfigPath = Path.Combine(image.MountedImagePath, "Foundry", "Runtime", "AutopilotHash", "OA3.cfg");
        string oa3InputPath = Path.Combine(image.MountedImagePath, "Foundry", "Runtime", "AutopilotHash", "input.xml");
        Assert.True(File.Exists(oa3ConfigPath));
        Assert.True(File.Exists(oa3InputPath));
        Assert.Equal("OA3", XDocument.Load(oa3ConfigPath).Root?.Name.LocalName);
        Assert.Equal("Key", XDocument.Load(oa3InputPath).Root?.Name.LocalName);
        Assert.False(Directory.Exists(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Autopilot")));
        Assert.False(File.Exists(Path.Combine(image.System32Path, "PCPKsp.dll")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenHardwareHashModeHasMissingOa3Tool_ReturnsFailure()
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
                PSBootstrapperSourceExecutablePath = image.PSBootstrapperSourcePath,
                IanaWindowsTimeZoneMapJson = "{}",
                AutopilotProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
                Oa3ToolSourcePath = Path.Combine(image.RootPath, "missing", "oa3tool.exe")
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("OA3Tool", result.Error?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProvisionAsync_DoesNotCreateRuntimeOwnedLogTempOrNetworkDirectories()
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
                PSBootstrapperSourceExecutablePath = image.PSBootstrapperSourcePath,
                IanaWindowsTimeZoneMapJson = "{}"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.False(Directory.Exists(Path.Combine(image.MountedImagePath, "Foundry", "Logs")));
        Assert.False(Directory.Exists(Path.Combine(image.MountedImagePath, "Foundry", "Temp")));
        Assert.False(Directory.Exists(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Network")));
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
                PSBootstrapperSourceExecutablePath = image.PSBootstrapperSourcePath,
                IanaWindowsTimeZoneMapJson = "{}"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        string deployConfigurationJson = await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "foundry.deploy.config.json"));
        using JsonDocument document = JsonDocument.Parse(deployConfigurationJson);
        JsonElement root = document.RootElement;
        Assert.Equal(
            Foundry.Core.Models.Configuration.Deploy.FoundryDeployConfigurationDocument.CurrentSchemaVersion,
            root.GetProperty("schemaVersion").GetInt32());
        Assert.True(root.TryGetProperty("localization", out _));
        Assert.True(root.TryGetProperty("customization", out JsonElement customization));
        Assert.True(customization.TryGetProperty("oobe", out _));
        Assert.True(customization.TryGetProperty("appxRemoval", out _));
        Assert.True(customization.TryGetProperty("aiComponentRemoval", out _));
        Assert.True(root.TryGetProperty("autopilot", out _));
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
                PSBootstrapperSourceExecutablePath = image.PSBootstrapperSourcePath,
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
        Assert.True(root.TryGetProperty("network", out JsonElement network));
        Assert.True(network.TryGetProperty("profileRoaming", out JsonElement profileRoaming));
        Assert.False(profileRoaming.GetProperty("isEnabled").GetBoolean());
        Assert.False(profileRoaming.GetProperty("includePrivateKeyMaterial").GetBoolean());
    }

    [Fact]
    public async Task ProvisionAsync_WhenMediaSecretKeyIsProvided_WritesSecretKeyUnderConfigSecrets()
    {
        using TempMountedImage image = TempMountedImage.Create();
        string curlSourcePath = Path.Combine(image.RootPath, "curl.exe");
        File.WriteAllText(curlSourcePath, "curl");
        byte[] secretKey = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();

        var service = new WinPeMountedImageAssetProvisioningService();

        WinPeResult result = await service.ProvisionAsync(
            new WinPeMountedImageAssetProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                Architecture = WinPeArchitecture.X64,
                BootstrapScriptContent = "bootstrap",
                CurlExecutableSourcePath = curlSourcePath,
                PSBootstrapperSourceExecutablePath = image.PSBootstrapperSourcePath,
                IanaWindowsTimeZoneMapJson = "{}",
                FoundryConnectConfigurationJson = CreateConnectConfigurationWithEncryptedSecret(),
                MediaSecretsKey = secretKey
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(secretKey, await File.ReadAllBytesAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Secrets", "media-secrets.key")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenDeployConfigurationHasEncryptedSecret_WritesSecretKeyUnderConfigSecrets()
    {
        using TempMountedImage image = TempMountedImage.Create();
        string curlSourcePath = Path.Combine(image.RootPath, "curl.exe");
        File.WriteAllText(curlSourcePath, "curl");
        byte[] secretKey = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();

        var service = new WinPeMountedImageAssetProvisioningService();

        WinPeResult result = await service.ProvisionAsync(
            new WinPeMountedImageAssetProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                Architecture = WinPeArchitecture.X64,
                BootstrapScriptContent = "bootstrap",
                CurlExecutableSourcePath = curlSourcePath,
                PSBootstrapperSourceExecutablePath = image.PSBootstrapperSourcePath,
                IanaWindowsTimeZoneMapJson = "{}",
                DeployConfigurationJson = CreateDeployConfigurationWithEncryptedSecret(),
                MediaSecretsKey = secretKey
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(secretKey, await File.ReadAllBytesAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Secrets", "media-secrets.key")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenMediaSecretKeyHasNoEncryptedSecret_ReturnsFailure()
    {
        using TempMountedImage image = TempMountedImage.Create();
        string curlSourcePath = Path.Combine(image.RootPath, "curl.exe");
        File.WriteAllText(curlSourcePath, "curl");
        byte[] secretKey = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();

        var service = new WinPeMountedImageAssetProvisioningService();

        WinPeResult result = await service.ProvisionAsync(
            new WinPeMountedImageAssetProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                Architecture = WinPeArchitecture.X64,
                BootstrapScriptContent = "bootstrap",
                CurlExecutableSourcePath = curlSourcePath,
                PSBootstrapperSourceExecutablePath = image.PSBootstrapperSourcePath,
                IanaWindowsTimeZoneMapJson = "{}",
                MediaSecretsKey = secretKey
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("must not be provisioned without encrypted", result.Error?.Details, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Secrets")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenEncryptedSecretHasNoMediaSecretKey_ReturnsFailure()
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
                PSBootstrapperSourceExecutablePath = image.PSBootstrapperSourcePath,
                IanaWindowsTimeZoneMapJson = "{}",
                FoundryConnectConfigurationJson = CreateConnectConfigurationWithEncryptedSecret()
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("require a media secret key", result.Error?.Details, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Secrets")));
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
                PSBootstrapperSourceExecutablePath = image.PSBootstrapperSourcePath,
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
                PSBootstrapperSourceExecutablePath = image.PSBootstrapperSourcePath,
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

    [Fact]
    public async Task ProvisionAsync_WhenAdditionalRootFoldersProvided_CopiesContentsPreservingStructure()
    {
        using TempMountedImage image = TempMountedImage.Create();
        string curlSourcePath = Path.Combine(image.RootPath, "curl.exe");
        File.WriteAllText(curlSourcePath, "curl");

        string extrasRoot = Path.Combine(image.RootPath, "extras");
        string nestedDir = Path.Combine(extrasRoot, "Windows", "System32");
        Directory.CreateDirectory(nestedDir);
        File.WriteAllText(Path.Combine(extrasRoot, "root.txt"), "root");
        File.WriteAllText(Path.Combine(nestedDir, "tool.dll"), "tool");

        var service = new WinPeMountedImageAssetProvisioningService();

        WinPeResult result = await service.ProvisionAsync(
            new WinPeMountedImageAssetProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                Architecture = WinPeArchitecture.X64,
                BootstrapScriptContent = "bootstrap",
                CurlExecutableSourcePath = curlSourcePath,
                PSBootstrapperSourceExecutablePath = image.PSBootstrapperSourcePath,
                IanaWindowsTimeZoneMapJson = "{}",
                AdditionalRootFolderSourcePaths = [extrasRoot]
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal("root", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "root.txt")));
        Assert.Equal("tool", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Windows", "System32", "tool.dll")));
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

        public string PSBootstrapperSourcePath
        {
            get
            {
                string path = Path.Combine(RootPath, "psbootstrapper.exe");
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, "psbootstrapper");
                }

                return path;
            }
        }

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

    private static string CreateConnectConfigurationWithEncryptedSecret()
    {
        return """
        {
          "schemaVersion": 1,
          "capabilities": {
            "wifiProvisioned": true
          },
          "dot1x": {},
          "wifi": {
            "isEnabled": true,
            "ssid": "Corp WiFi",
            "securityType": "WPA2/WPA3-Personal",
            "passphraseSecret": {
              "kind": "encrypted",
              "algorithm": "aes-gcm-v1",
              "keyId": "media",
              "nonce": "AAAAAAAAAAAAAAAA",
              "tag": "AAAAAAAAAAAAAAAAAAAAAA",
              "ciphertext": "AAAAAAAA"
            }
          },
          "internetProbe": {
            "probeUris": [
              "http://www.msftconnecttest.com/connecttest.txt"
            ],
            "timeoutSeconds": 5
          }
        }
        """;
    }

    private static string CreateDeployConfigurationWithEncryptedSecret()
    {
        return """
        {
          "schemaVersion": 1,
          "autopilot": {
            "hardwareHashUpload": {
              "pfxSecret": {
                "kind": "encrypted",
                "algorithm": "aes-gcm-v1",
                "keyId": "media",
                "nonce": "AAAAAAAAAAAAAAAA",
                "tag": "AAAAAAAAAAAAAAAAAAAAAA",
                "ciphertext": "AAAAAAAA"
              }
            }
          }
        }
        """;
    }
}
