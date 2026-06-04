using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Models.Network;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Network;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class NetworkProfileRoamingArtifactServiceTests
{
    [Fact]
    public async Task LoadAsync_WhenPrivateKeyRoamingEnabled_StagesProfilesCertificatesAndPfxPassword()
    {
        string artifactRoot = CreateArtifactRoot();
        byte[] mediaSecretKey = RandomNumberGenerator.GetBytes(DeployMediaSecretEnvelopeProtector.KeySizeBytes);
        WriteText(Path.Combine(artifactRoot, "wifi-profile.xml"), "<WLANProfile />");
        WriteText(Path.Combine(artifactRoot, "wired-dot1x-profile.xml"), "<LANProfile />");
        WriteBytes(Path.Combine(artifactRoot, "certificates", "Root", "root.cer"), [1, 2, 3]);
        WriteBytes(Path.Combine(artifactRoot, "certificates", "My", "client.pfx"), [4, 5, 6]);
        WriteSecret(
            Path.Combine(artifactRoot, "certificates", "My", "client.pfx.password.json"),
            EncryptString("pfx-password", mediaSecretKey));
        WriteText(
            Path.Combine(artifactRoot, "manifest.json"),
            """
            {
              "schemaVersion": 1,
              "wifiProfile": {
                "relativePath": "wifi-profile.xml",
                "source": "manualWifi",
                "connectivityExpectation": "preOobeConnectable"
              },
              "wiredDot1xProfile": {
                "relativePath": "wired-dot1x-profile.xml",
                "source": "provisionedWiredDot1x",
                "connectivityExpectation": "dependsOnMachineCredential"
              },
              "certificates": [
                {
                  "relativePath": "certificates\\Root\\root.cer",
                  "kind": "publicCertificate",
                  "storeName": "Root"
                },
                {
                  "relativePath": "certificates\\My\\client.pfx",
                  "kind": "pfxPrivateKey",
                  "storeName": "My",
                  "passwordSecretRelativePath": "certificates\\My\\client.pfx.password.json"
                }
              ]
            }
            """);
        var service = new NetworkProfileRoamingArtifactService(
            new FakeMediaSecretKeyReader(mediaSecretKey),
            NullLogger<NetworkProfileRoamingArtifactService>.Instance);

        var payload = await service.LoadAsync(
            new DeployNetworkProfileRoamingSettings
            {
                IsEnabled = true,
                IncludePrivateKeyMaterial = true,
                ArtifactRootPath = artifactRoot
            },
            Path.Combine(artifactRoot, "workspace"),
            TestContext.Current.CancellationToken);

        Assert.NotNull(payload);
        Assert.Contains(payload.DataFiles, file => file.FileName == Path.Combine("NetworkProfiles", "wifi-profile.xml"));
        Assert.Contains(payload.DataFiles, file => file.FileName == Path.Combine("NetworkProfiles", "wired-dot1x-profile.xml"));
        Assert.Contains(payload.DataFiles, file => file.FileName == Path.Combine("NetworkProfiles", "certificates", "Root", "root.cer") && file.Bytes is [1, 2, 3]);
        Assert.Contains(payload.DataFiles, file => file.FileName == Path.Combine("NetworkProfiles", "certificates", "My", "client.pfx") && file.IsSensitive);
        PreOobeDataFile passwordFile = Assert.Single(
            payload.DataFiles
                .Where(file => file.FileName == Path.Combine("NetworkProfiles", "certificates", "My", "client.pfx.password"))
                .Select(file => new PreOobeDataFile(file.FileName, file.Content, file.IsSensitive)));
        Assert.Equal("pfx-password", passwordFile.Content);
        Assert.True(passwordFile.IsSensitive);

        string importSettingsJson = Assert.Single(payload.DataFiles, file => file.FileName == Path.Combine("NetworkProfiles", "import-settings.json")).Content;
        using JsonDocument importSettings = JsonDocument.Parse(importSettingsJson);
        Assert.Equal(Path.Combine("NetworkProfiles", "wifi-profile.xml"), importSettings.RootElement.GetProperty("wifiProfileRelativePath").GetString());
        Assert.Equal(Path.Combine("NetworkProfiles", "wired-dot1x-profile.xml"), importSettings.RootElement.GetProperty("wiredDot1xProfileRelativePath").GetString());
        JsonElement certificates = importSettings.RootElement.GetProperty("certificates");
        Assert.Equal(2, certificates.GetArrayLength());
        Assert.Contains(
            certificates.EnumerateArray(),
            certificate => certificate.TryGetProperty("passwordRelativePath", out JsonElement passwordRelativePath) &&
                passwordRelativePath.GetString() == Path.Combine("NetworkProfiles", "certificates", "My", "client.pfx.password"));
    }

    [Fact]
    public async Task LoadAsync_WhenPrivateKeyRoamingDisabled_SkipsPfxMaterial()
    {
        string artifactRoot = CreateArtifactRoot();
        WriteBytes(Path.Combine(artifactRoot, "certificates", "Root", "root.cer"), [1]);
        WriteBytes(Path.Combine(artifactRoot, "certificates", "My", "client.pfx"), [2]);
        WriteText(
            Path.Combine(artifactRoot, "manifest.json"),
            """
            {
              "schemaVersion": 1,
              "certificates": [
                {
                  "relativePath": "certificates\\Root\\root.cer",
                  "kind": "publicCertificate",
                  "storeName": "Root"
                },
                {
                  "relativePath": "certificates\\My\\client.pfx",
                  "kind": "pfxPrivateKey",
                  "storeName": "My",
                  "passwordSecretRelativePath": "certificates\\My\\client.pfx.password.json"
                }
              ]
            }
            """);
        var service = new NetworkProfileRoamingArtifactService(
            new FakeMediaSecretKeyReader([]),
            NullLogger<NetworkProfileRoamingArtifactService>.Instance);

        var payload = await service.LoadAsync(
            new DeployNetworkProfileRoamingSettings
            {
                IsEnabled = true,
                IncludePrivateKeyMaterial = false,
                ArtifactRootPath = artifactRoot
            },
            Path.Combine(artifactRoot, "workspace"),
            TestContext.Current.CancellationToken);

        Assert.NotNull(payload);
        Assert.Contains(payload.DataFiles, file => file.FileName == Path.Combine("NetworkProfiles", "certificates", "Root", "root.cer"));
        Assert.DoesNotContain(payload.DataFiles, file => file.FileName.Contains("client.pfx", StringComparison.OrdinalIgnoreCase));
    }

    private static string CreateArtifactRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "FoundryDeployNetworkTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void WriteBytes(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    private static void WriteSecret(string path, SecretEnvelope envelope)
    {
        WriteText(path, JsonSerializer.Serialize(envelope, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private static SecretEnvelope EncryptString(string plaintext, byte[] key)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] ciphertext = new byte[plaintextBytes.Length];
        byte[] tag = new byte[16];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        return new SecretEnvelope
        {
            Kind = DeployMediaSecretEnvelopeProtector.Kind,
            Algorithm = DeployMediaSecretEnvelopeProtector.Algorithm,
            KeyId = DeployMediaSecretEnvelopeProtector.KeyId,
            Nonce = Base64UrlEncode(nonce),
            Tag = Base64UrlEncode(tag),
            Ciphertext = Base64UrlEncode(ciphertext)
        };
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed class FakeMediaSecretKeyReader(byte[] key) : IMediaSecretKeyReader
    {
        public Task<byte[]> ReadAsync(string workspaceRootPath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(key);
        }
    }

    private sealed record PreOobeDataFile(string FileName, string Content, bool IsSensitive);
}
