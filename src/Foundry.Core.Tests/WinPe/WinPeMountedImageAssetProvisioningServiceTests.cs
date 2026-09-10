// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;
using Foundry.Telemetry;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeMountedImageAssetProvisioningServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProvisionAsync_WritesIsolatedBootstrapTelemetryConfiguration(bool provideConfiguration)
    {
        using TempMountedImage image = TempMountedImage.Create();
        string curlSource = Path.Combine(image.RootPath, "curl.exe");
        File.WriteAllText(curlSource, "curl");
        var configuration = new FoundryBootstrapConfigurationDocument
        {
            Telemetry = new TelemetrySettings
            {
                IsEnabled = true,
                IsRemoteDiagnosticsEnabled = false,
                InstallId = "anonymous-install",
                RuntimePayloadSource = TelemetryRuntimePayloadSources.Release
            }
        };

        WinPeResult result = await new WinPeMountedImageAssetProvisioningService().ProvisionAsync(
            new WinPeMountedImageAssetProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                CurlExecutableSourcePath = curlSource,
                IanaWindowsTimeZoneMapJson = "{}",
                FoundryBootstrapConfiguration = provideConfiguration ? configuration : null,
                FoundryConnectConfigurationJson = "{\"network\":{\"secret\":\"network-secret\"}}",
                DeployConfigurationJson = "{\"deploymentSecret\":\"deployment-secret\"}"
            }, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Details);
        string json = await File.ReadAllTextAsync(
            Path.Combine(image.MountedImagePath, "Foundry", "Config", "foundry.bootstrap.config.json"),
            TestContext.Current.CancellationToken);
        var written = JsonSerializer.Deserialize<FoundryBootstrapConfigurationDocument>(json, ConfigurationJsonDefaults.SerializerOptions);
        Assert.NotNull(written);
        Assert.Equal(1, written.SchemaVersion);
        Assert.Equal(provideConfiguration, written.Telemetry.IsEnabled);
        Assert.False(written.Telemetry.IsRemoteDiagnosticsEnabled);
        if (provideConfiguration)
        {
            Assert.Equal(configuration.Telemetry, written.Telemetry);
        }

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(["schemaVersion", "telemetry"], document.RootElement.EnumerateObject().Select(property => property.Name).Order());
        Assert.DoesNotContain("network-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("deployment-secret", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Launcher_WhenExecutableCannotStart_RecordsAttemptAndPropagatesFailure()
    {
        using TempMountedImage image = TempMountedImage.Create();
        string curlSource = Path.Combine(image.RootPath, "curl.exe");
        File.WriteAllText(curlSource, "curl");
        WinPeResult result = await new WinPeMountedImageAssetProvisioningService().ProvisionAsync(
            new WinPeMountedImageAssetProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                CurlExecutableSourcePath = curlSource,
                IanaWindowsTimeZoneMapJson = "{}"
            }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error?.Details);

        string foundryRoot = Path.Combine(image.MountedImagePath, "Foundry");
        string launcherPath = Path.Combine(foundryRoot, "Bootstrap", "Launch.cmd");
        string launcher = File.ReadAllText(launcherPath).Replace(@"X:\Foundry", foundryRoot, StringComparison.Ordinal);
        File.WriteAllText(launcherPath, launcher);
        File.Delete(Path.Combine(foundryRoot, "Bootstrap", "Foundry.Bootstrap.exe"));

        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c \"\"{launcherPath}\"\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        await Task.WhenAll(output, error);

        Assert.NotEqual(0, process.ExitCode);
        string log = File.ReadAllText(Path.Combine(foundryRoot, "Logs", "FoundryBootstrap.Launcher.log"));
        Assert.Contains("Starting Foundry.Bootstrap.exe", log, StringComparison.Ordinal);
        Assert.Contains($"exited with code {process.ExitCode}", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisionAsync_WhenBootstrapIsMissing_DoesNotCreateLauncher()
    {
        using TempMountedImage image = TempMountedImage.Create();
        File.Delete(Path.Combine(image.MountedImagePath, "Foundry", "Bootstrap", "Foundry.Bootstrap.exe"));

        WinPeResult result = await new WinPeMountedImageAssetProvisioningService().ProvisionAsync(
            new WinPeMountedImageAssetProvisioningOptions { MountedImagePath = image.MountedImagePath },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("Foundry.Bootstrap", result.Error!.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(image.System32Path, "startnet.cmd")));
    }

    [Fact]
    public async Task ProvisionAsync_WritesBootstrapStartnetAndCurl()
    {
        using TempMountedImage image = TempMountedImage.Create();
        string curlSourcePath = Path.Combine(image.RootPath, "tools", "curl.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(curlSourcePath)!);
        File.WriteAllText(curlSourcePath, "curl");
        string startnetPath = Path.Combine(image.MountedImagePath, "Windows", "System32", "startnet.cmd");
        File.WriteAllLines(startnetPath, ["wpeinit", "echo existing", @"powershell.exe -File X:\Windows\System32\FoundryBootstrap.ps1"]);
        File.WriteAllText(Path.Combine(image.System32Path, "FoundryBootstrap.ps1"), "legacy");

        var service = new WinPeMountedImageAssetProvisioningService();

        WinPeResult result = await service.ProvisionAsync(
            new WinPeMountedImageAssetProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                Architecture = WinPeArchitecture.X64,
                CurlExecutableSourcePath = curlSourcePath,
                IanaWindowsTimeZoneMapJson = "{}"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.False(File.Exists(Path.Combine(image.System32Path, "FoundryBootstrap.ps1")));
        string launcher = await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Bootstrap", "Launch.cmd"), TestContext.Current.CancellationToken);
        Assert.Contains("Foundry.Bootstrap.exe", launcher, StringComparison.Ordinal);
        Assert.Equal("curl", await File.ReadAllTextAsync(Path.Combine(image.System32Path, "curl.exe"), TestContext.Current.CancellationToken));

        string[] startnetLines = await File.ReadAllLinesAsync(startnetPath, TestContext.Current.CancellationToken);
        Assert.Contains(startnetLines, line => line.Equals("wpeinit", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(startnetLines, line => line.Equals("echo existing", StringComparison.OrdinalIgnoreCase));
        Assert.Single(startnetLines, line => line.Equals(@"call X:\Foundry\Bootstrap\Launch.cmd", StringComparison.OrdinalIgnoreCase));
        Assert.True(Array.IndexOf(startnetLines, "wpeinit") < Array.IndexOf(startnetLines, @"call X:\Foundry\Bootstrap\Launch.cmd"));
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
            CurlExecutableSourcePath = curlSourcePath,
            IanaWindowsTimeZoneMapJson = "{}"
        };

        WinPeResult firstResult = await service.ProvisionAsync(options, CancellationToken.None);
        WinPeResult secondResult = await service.ProvisionAsync(options, CancellationToken.None);

        Assert.True(firstResult.IsSuccess, firstResult.Error?.Details);
        Assert.True(secondResult.IsSuccess, secondResult.Error?.Details);

        string[] startnetLines = await File.ReadAllLinesAsync(Path.Combine(image.System32Path, "startnet.cmd"), TestContext.Current.CancellationToken);
        Assert.Single(startnetLines, line => line.Equals(@"call X:\Foundry\Bootstrap\Launch.cmd", StringComparison.OrdinalIgnoreCase));
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
                CurlExecutableSourcePath = curlSourcePath,
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
        Assert.Equal("{\"schemaVersion\":1}", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "foundry.connect.config.json"), TestContext.Current.CancellationToken));
        Assert.Equal("{\"schemaVersion\":2}", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "foundry.deploy.config.json"), TestContext.Current.CancellationToken));
        Assert.Equal("debug", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "foundry.connect.provisioning-source.txt"), TestContext.Current.CancellationToken));
        Assert.Equal("release", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "foundry.deploy.provisioning-source.txt"), TestContext.Current.CancellationToken));
        Assert.Equal("{\"zones\":[]}", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "iana-windows-timezones.json"), TestContext.Current.CancellationToken));
        Assert.Equal("<WLANProfile />", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Network", "Wifi", "Profiles", "profile.xml"), TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Network", "Wired")));
        Assert.False(Directory.Exists(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Network", "Certificates")));
        Assert.Equal("{\"profile\":1}", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Autopilot", "Profile1", "AutopilotConfigurationFile.json"), TestContext.Current.CancellationToken));
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
                CurlExecutableSourcePath = curlSourcePath,
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
                CurlExecutableSourcePath = curlSourcePath,
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
        Assert.Equal("oa3", await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Tools", "OA3", "oa3tool.exe"), TestContext.Current.CancellationToken));
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
                CurlExecutableSourcePath = curlSourcePath,
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
                CurlExecutableSourcePath = curlSourcePath,
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
                CurlExecutableSourcePath = curlSourcePath,
                IanaWindowsTimeZoneMapJson = "{}"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        string deployConfigurationJson = await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "foundry.deploy.config.json"), TestContext.Current.CancellationToken);
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
                CurlExecutableSourcePath = curlSourcePath,
                IanaWindowsTimeZoneMapJson = "{}"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        string connectConfigurationJson = await File.ReadAllTextAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "foundry.connect.config.json"), TestContext.Current.CancellationToken);
        using JsonDocument document = JsonDocument.Parse(connectConfigurationJson);
        JsonElement root = document.RootElement;
        Assert.True(root.TryGetProperty("schemaVersion", out _));
        Assert.True(root.TryGetProperty("capabilities", out _));
        Assert.True(root.TryGetProperty("dot1x", out _));
        Assert.True(root.TryGetProperty("wifi", out _));
        Assert.True(root.TryGetProperty("internetProbe", out _));
        Assert.True(root.TryGetProperty("network", out JsonElement network));
        Assert.True(network.TryGetProperty("profileRoaming", out JsonElement profileRoaming));
        Assert.False(profileRoaming.GetProperty("wiredDot1x").GetProperty("isEnabled").GetBoolean());
        Assert.False(profileRoaming.GetProperty("wifi").GetProperty("isEnabled").GetBoolean());
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
                CurlExecutableSourcePath = curlSourcePath,
                IanaWindowsTimeZoneMapJson = "{}",
                FoundryConnectConfigurationJson = CreateConnectConfigurationWithEncryptedSecret(),
                NetworkSecretsKey = secretKey
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(secretKey, await File.ReadAllBytesAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Secrets", "media-secrets.key"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProvisionAsync_WhenUnprotectedDeploymentKeyIsProvided_WritesSeparateDeploymentKey()
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
                CurlExecutableSourcePath = curlSourcePath,
                IanaWindowsTimeZoneMapJson = "{}",
                DeploymentSecretsKey = secretKey
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal(secretKey, await File.ReadAllBytesAsync(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Secrets", "deployment-secrets.key"), TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Secrets", "media-secrets.key")));
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
                CurlExecutableSourcePath = curlSourcePath,
                IanaWindowsTimeZoneMapJson = "{}",
                NetworkSecretsKey = secretKey
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
                CurlExecutableSourcePath = curlSourcePath,
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
                CurlExecutableSourcePath = curlSourcePath,
                IanaWindowsTimeZoneMapJson = "{}"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.False(Directory.Exists(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Secrets")));
    }

    [Fact]
    public async Task ProvisionAsync_WhenDeploymentProtectionIsEnabled_EncryptsProfilesWithoutWritingDeploymentKey()
    {
        using TempMountedImage image = TempMountedImage.Create();
        string curlSourcePath = Path.Combine(image.RootPath, "curl.exe");
        File.WriteAllText(curlSourcePath, "curl");
        byte[] deploymentKey = Enumerable.Range(32, 32).Select(static value => (byte)value).ToArray();
        const string profileJson = "{\"Comment_File\":\"Protected profile\"}";
        var service = new WinPeMountedImageAssetProvisioningService();

        WinPeResult result = await service.ProvisionAsync(
            new WinPeMountedImageAssetProvisioningOptions
            {
                MountedImagePath = image.MountedImagePath,
                Architecture = WinPeArchitecture.X64,
                CurlExecutableSourcePath = curlSourcePath,
                IanaWindowsTimeZoneMapJson = "{}",
                IsDeploymentProtectionEnabled = true,
                DeploymentSecretsKey = deploymentKey,
                AutopilotProfiles =
                [
                    new AutopilotProfileSettings
                    {
                        Id = "protected-profile",
                        DisplayName = "Protected profile",
                        FolderName = "ProtectedProfile",
                        Source = "import",
                        ImportedAtUtc = DateTimeOffset.UtcNow,
                        JsonContent = profileJson
                    }
                ]
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        string profileRoot = Path.Combine(image.MountedImagePath, "Foundry", "Config", "Autopilot", "ProtectedProfile");
        string encryptedPath = Path.Combine(profileRoot, "AutopilotConfigurationFile.json.encrypted");
        Assert.True(File.Exists(encryptedPath));
        Assert.False(File.Exists(Path.Combine(profileRoot, "AutopilotConfigurationFile.json")));
        Assert.False(File.Exists(Path.Combine(image.MountedImagePath, "Foundry", "Config", "Secrets", "deployment-secrets.key")));

        SecretEnvelope envelope = JsonSerializer.Deserialize<SecretEnvelope>(
            await File.ReadAllTextAsync(encryptedPath, TestContext.Current.CancellationToken),
            ConfigurationJsonDefaults.SerializerOptions)!;
        byte[] plaintext = MediaSecretEnvelopeProtector.DecryptBytes(
            envelope,
            deploymentKey,
            MediaSecretEnvelopeProtector.DeploymentKeyId);
        try
        {
            Assert.Equal(profileJson, Encoding.UTF8.GetString(plaintext));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
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
                CurlExecutableSourcePath = curlSourcePath,
                SevenZipSourceDirectoryPath = sevenZipSourcePath,
                IanaWindowsTimeZoneMapJson = "{}"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        string toolsPath = Path.Combine(image.MountedImagePath, "Foundry", "Tools", "7zip");
        Assert.Equal("7za", await File.ReadAllTextAsync(Path.Combine(toolsPath, "x64", "7za.exe"), TestContext.Current.CancellationToken));
        Assert.Equal("license", await File.ReadAllTextAsync(Path.Combine(toolsPath, "License.txt"), TestContext.Current.CancellationToken));
        Assert.Equal("readme", await File.ReadAllTextAsync(Path.Combine(toolsPath, "readme.txt"), TestContext.Current.CancellationToken));
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
            string bootstrapDirectory = Path.Combine(mountedImagePath, "Foundry", "Bootstrap");
            Directory.CreateDirectory(bootstrapDirectory);
            File.WriteAllText(Path.Combine(bootstrapDirectory, "Foundry.Bootstrap.exe"), "bootstrap");
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
