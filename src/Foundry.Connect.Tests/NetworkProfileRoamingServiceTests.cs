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
                NetworkProfileRoamingProfileSource.ProvisionedWiredDot1x,
                NetworkProfileRoamingConnectivityExpectation.DependsOnMachineCredential,
                [certificatePath]),
            TestContext.Current.CancellationToken);

        string copiedProfilePath = Path.Combine(tempDirectory.ArtifactPath, "wired-dot1x-profile.xml");
        Assert.True(File.Exists(copiedProfilePath));

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(tempDirectory.ArtifactPath, "manifest.json")));
        JsonElement wiredProfile = manifest.RootElement.GetProperty("wiredDot1xProfile");
        Assert.Equal("wired-dot1x-profile.xml", wiredProfile.GetProperty("relativePath").GetString());
        Assert.Equal("dependsOnMachineCredential", wiredProfile.GetProperty("connectivityExpectation").GetString());

        JsonElement certificate = manifest.RootElement.GetProperty("certificates").EnumerateArray().Single();
        string certificateRelativePath = certificate.GetProperty("relativePath").GetString()!;
        Assert.StartsWith(@"certificates\Root\provisionedWiredDot1x\root-", certificateRelativePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".cer", certificateRelativePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("publicCertificate", certificate.GetProperty("kind").GetString());
        Assert.Equal("Root", certificate.GetProperty("storeName").GetString());
        Assert.Equal("certificate-bytes", File.ReadAllText(Path.Combine(tempDirectory.ArtifactPath, certificateRelativePath)));
    }

    [Fact]
    public async Task CaptureWiredDot1xProfileAsync_WhenPfxRoamingIsEnabled_CopiesPfxAndEncryptedPasswordSecret()
    {
        using var tempDirectory = new TemporaryDirectory();
        string profilePath = tempDirectory.CreateFile("source", "wired.xml", "<LANProfile />");
        string certificatePath = tempDirectory.CreateFile("source", "machine.pfx", "pfx-bytes");
        var service = new NetworkProfileRoamingService(
            CreateEnabledConfiguration(includePrivateKeyMaterial: true),
            NullLogger<NetworkProfileRoamingService>.Instance,
            tempDirectory.ArtifactPath);

        await service.CaptureWiredDot1xProfileAsync(
            new NetworkProfileRoamingCaptureRequest(
                profilePath,
                NetworkProfileRoamingProfileSource.ProvisionedWiredDot1x,
                NetworkProfileRoamingConnectivityExpectation.DependsOnMachineCredential,
                [certificatePath],
                CreateSecretEnvelope()),
            TestContext.Current.CancellationToken);

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(tempDirectory.ArtifactPath, "manifest.json")));
        JsonElement certificate = manifest.RootElement.GetProperty("certificates").EnumerateArray().Single();
        string certificateRelativePath = certificate.GetProperty("relativePath").GetString()!;
        string passwordSecretRelativePath = certificate.GetProperty("passwordSecretRelativePath").GetString()!;
        Assert.StartsWith(@"certificates\My\provisionedWiredDot1x\machine-", certificateRelativePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".pfx", certificateRelativePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("pfxPrivateKey", certificate.GetProperty("kind").GetString());
        Assert.Equal("My", certificate.GetProperty("storeName").GetString());
        Assert.Equal($"{certificateRelativePath}.password.json", passwordSecretRelativePath);
        Assert.True(File.Exists(Path.Combine(tempDirectory.ArtifactPath, certificateRelativePath)));
        Assert.True(File.Exists(Path.Combine(tempDirectory.ArtifactPath, passwordSecretRelativePath)));
    }

    [Fact]
    public async Task CaptureWiredDot1xProfileAsync_WhenSourceIsRecapturedWithoutPfx_RemovesStalePrivateKeyMaterial()
    {
        using var tempDirectory = new TemporaryDirectory();
        string profilePath = tempDirectory.CreateFile("source", "wired.xml", "<LANProfile />");
        string certificatePath = tempDirectory.CreateFile("source", "machine.pfx", "pfx-bytes");
        var firstService = new NetworkProfileRoamingService(
            CreateEnabledConfiguration(includePrivateKeyMaterial: true),
            NullLogger<NetworkProfileRoamingService>.Instance,
            tempDirectory.ArtifactPath);

        await firstService.CaptureWiredDot1xProfileAsync(
            new NetworkProfileRoamingCaptureRequest(
                profilePath,
                NetworkProfileRoamingProfileSource.ProvisionedWiredDot1x,
                NetworkProfileRoamingConnectivityExpectation.DependsOnMachineCredential,
                [certificatePath],
                CreateSecretEnvelope()),
            TestContext.Current.CancellationToken);

        string[] copiedPfxPaths = Directory.GetFiles(Path.Combine(tempDirectory.ArtifactPath, "certificates", "My"), "*.pfx", SearchOption.AllDirectories);
        string[] copiedPasswordPaths = Directory.GetFiles(Path.Combine(tempDirectory.ArtifactPath, "certificates", "My"), "*.password.json", SearchOption.AllDirectories);
        Assert.Single(copiedPfxPaths);
        Assert.Single(copiedPasswordPaths);

        var secondService = new NetworkProfileRoamingService(
            CreateEnabledConfiguration(),
            NullLogger<NetworkProfileRoamingService>.Instance,
            tempDirectory.ArtifactPath);

        await secondService.CaptureWiredDot1xProfileAsync(
            new NetworkProfileRoamingCaptureRequest(
                profilePath,
                NetworkProfileRoamingProfileSource.ProvisionedWiredDot1x,
                NetworkProfileRoamingConnectivityExpectation.DependsOnMachineCredential),
            TestContext.Current.CancellationToken);

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(tempDirectory.ArtifactPath, "manifest.json")));
        Assert.Empty(manifest.RootElement.GetProperty("certificates").EnumerateArray());
        Assert.False(File.Exists(copiedPfxPaths.Single()));
        Assert.False(File.Exists(copiedPasswordPaths.Single()));
    }

    [Fact]
    public async Task CaptureProfilesAsync_WhenCertificatesShareFileName_StagesDistinctArtifacts()
    {
        using var tempDirectory = new TemporaryDirectory();
        string wiredProfilePath = tempDirectory.CreateFile("wired", "wired.xml", "<LANProfile />");
        string wifiProfilePath = tempDirectory.CreateFile("wifi", "wifi.xml", "<WLANProfile />");
        string wiredCertificatePath = tempDirectory.CreateFile("wired", "root.cer", "wired-certificate");
        string wifiCertificatePath = tempDirectory.CreateFile("wifi", "root.cer", "wifi-certificate");
        var service = new NetworkProfileRoamingService(
            CreateEnabledConfiguration(),
            NullLogger<NetworkProfileRoamingService>.Instance,
            tempDirectory.ArtifactPath);

        await service.CaptureWiredDot1xProfileAsync(
            new NetworkProfileRoamingCaptureRequest(
                wiredProfilePath,
                NetworkProfileRoamingProfileSource.ProvisionedWiredDot1x,
                NetworkProfileRoamingConnectivityExpectation.DependsOnMachineCredential,
                [wiredCertificatePath]),
            TestContext.Current.CancellationToken);

        await service.CaptureWifiProfileAsync(
            new NetworkProfileRoamingCaptureRequest(
                wifiProfilePath,
                NetworkProfileRoamingProfileSource.ProvisionedWifi,
                NetworkProfileRoamingConnectivityExpectation.ImportOnly,
                [wifiCertificatePath]),
            TestContext.Current.CancellationToken);

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(tempDirectory.ArtifactPath, "manifest.json")));
        string[] relativePaths = manifest.RootElement
            .GetProperty("certificates")
            .EnumerateArray()
            .Select(static certificate => certificate.GetProperty("relativePath").GetString())
            .OfType<string>()
            .ToArray();

        Assert.Equal(2, relativePaths.Length);
        Assert.Equal(2, relativePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(relativePaths, static path => path.Contains(@"provisionedWiredDot1x\", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(relativePaths, static path => path.Contains(@"provisionedWifi\", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(relativePaths, path => File.ReadAllText(Path.Combine(tempDirectory.ArtifactPath, path)) == "wired-certificate");
        Assert.Contains(relativePaths, path => File.ReadAllText(Path.Combine(tempDirectory.ArtifactPath, path)) == "wifi-certificate");
    }

    private static FoundryConnectConfiguration CreateEnabledConfiguration(bool includePrivateKeyMaterial = false)
    {
        return new FoundryConnectConfiguration
        {
            Network = new ConnectNetworkSettings
            {
                ProfileRoaming = new ConnectNetworkProfileRoamingSettings
                {
                    IsEnabled = true,
                    IncludePrivateKeyMaterial = includePrivateKeyMaterial
                }
            }
        };
    }

    private static SecretEnvelope CreateSecretEnvelope()
    {
        return new SecretEnvelope
        {
            Kind = "encrypted",
            Algorithm = "aes-gcm-v1",
            KeyId = "media",
            Nonce = "nonce",
            Tag = "tag",
            Ciphertext = "ciphertext"
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
