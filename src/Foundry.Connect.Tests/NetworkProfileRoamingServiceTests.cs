using System.Text.Json;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Services.Network;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Connect.Tests;

public sealed class NetworkProfileRoamingServiceTests
{
    [Fact]
    public async Task CaptureWifiProfileAsync_WhenRoamingIsDisabled_DoesNotCreateArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        string profilePath = tempDirectory.CreateFile("source", "wifi.xml", "<WLANProfile />");
        var service = new NetworkProfileRoamingService(
            new FoundryConnectConfiguration(),
            NullLogger<NetworkProfileRoamingService>.Instance,
            tempDirectory.ArtifactPath);

        await service.CaptureWifiProfileAsync(
            new NetworkProfileRoamingCaptureRequest(
                profilePath,
                NetworkProfileRoamingProfileKind.Wifi,
                NetworkProfileRoamingProfileSource.ManualWifi,
                NetworkProfileRoamingConnectivityExpectation.PreOobeConnectable),
            TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(tempDirectory.ArtifactPath));
    }

    [Fact]
    public async Task CaptureWifiProfileAsync_WhenManualWifiSucceeds_WritesWifiProfileAndManifest()
    {
        using var tempDirectory = new TemporaryDirectory();
        string profilePath = tempDirectory.CreateFile("source", "wifi.xml", "<WLANProfile><name>Guest</name></WLANProfile>");
        var service = new NetworkProfileRoamingService(
            CreateEnabledConfiguration(),
            NullLogger<NetworkProfileRoamingService>.Instance,
            tempDirectory.ArtifactPath);

        await service.CaptureWifiProfileAsync(
            new NetworkProfileRoamingCaptureRequest(
                profilePath,
                NetworkProfileRoamingProfileKind.Wifi,
                NetworkProfileRoamingProfileSource.ManualWifi,
                NetworkProfileRoamingConnectivityExpectation.PreOobeConnectable),
            TestContext.Current.CancellationToken);

        string copiedProfilePath = Path.Combine(tempDirectory.ArtifactPath, "wifi-profile.xml");
        Assert.True(File.Exists(copiedProfilePath));
        Assert.Equal("<WLANProfile><name>Guest</name></WLANProfile>", File.ReadAllText(copiedProfilePath));

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(tempDirectory.ArtifactPath, "manifest.json")));
        JsonElement wifiProfile = manifest.RootElement.GetProperty("wifiProfile");
        Assert.Equal("wifi-profile.xml", wifiProfile.GetProperty("relativePath").GetString());
        Assert.Equal("manualWifi", wifiProfile.GetProperty("source").GetString());
        Assert.Equal("preOobeConnectable", wifiProfile.GetProperty("connectivityExpectation").GetString());
    }

    [Fact]
    public async Task CaptureWiredDot1xProfileAsync_WhenCertificateIsProvided_CopiesRootCertificateWithManifestIntent()
    {
        using var tempDirectory = new TemporaryDirectory();
        string profilePath = tempDirectory.CreateFile("source", "wired.xml", "<LANProfile />");
        string certificatePath = tempDirectory.CreateFile("source", "root.cer", "certificate-bytes");
        var service = new NetworkProfileRoamingService(
            CreateEnabledConfiguration(),
            NullLogger<NetworkProfileRoamingService>.Instance,
            tempDirectory.ArtifactPath);

        await service.CaptureWiredDot1xProfileAsync(
            new NetworkProfileRoamingCaptureRequest(
                profilePath,
                NetworkProfileRoamingProfileKind.WiredDot1x,
                NetworkProfileRoamingProfileSource.ProvisionedWiredDot1x,
                NetworkProfileRoamingConnectivityExpectation.DependsOnMachineCredential,
                [certificatePath]),
            TestContext.Current.CancellationToken);

        string copiedProfilePath = Path.Combine(tempDirectory.ArtifactPath, "wired-dot1x-profile.xml");
        string copiedCertificatePath = Path.Combine(tempDirectory.ArtifactPath, "certificates", "Root", "root.cer");
        Assert.True(File.Exists(copiedProfilePath));
        Assert.True(File.Exists(copiedCertificatePath));
        Assert.Equal("certificate-bytes", File.ReadAllText(copiedCertificatePath));

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(tempDirectory.ArtifactPath, "manifest.json")));
        JsonElement wiredProfile = manifest.RootElement.GetProperty("wiredDot1xProfile");
        Assert.Equal("wired-dot1x-profile.xml", wiredProfile.GetProperty("relativePath").GetString());
        Assert.Equal("dependsOnMachineCredential", wiredProfile.GetProperty("connectivityExpectation").GetString());

        JsonElement certificate = manifest.RootElement.GetProperty("certificates").EnumerateArray().Single();
        Assert.Equal(@"certificates\Root\root.cer", certificate.GetProperty("relativePath").GetString());
        Assert.Equal("publicCertificate", certificate.GetProperty("kind").GetString());
        Assert.Equal("Root", certificate.GetProperty("storeName").GetString());
    }

    private static FoundryConnectConfiguration CreateEnabledConfiguration()
    {
        return new FoundryConnectConfiguration
        {
            Network = new ConnectNetworkSettings
            {
                ProfileRoaming = new ConnectNetworkProfileRoamingSettings
                {
                    IsEnabled = true
                }
            }
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Foundry.Connect.Tests", Guid.NewGuid().ToString("N"));
            ArtifactPath = System.IO.Path.Combine(Path, "artifact");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string ArtifactPath { get; }

        public string CreateFile(string directoryName, string fileName, string contents)
        {
            string directoryPath = System.IO.Path.Combine(Path, directoryName);
            Directory.CreateDirectory(directoryPath);
            string path = System.IO.Path.Combine(directoryPath, fileName);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
