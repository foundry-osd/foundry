using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Deployment;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class AutopilotHardwareHashUploadServiceTests
{
    [Fact]
    public async Task UploadAsync_WhenEncryptedCertificateMaterialIsMissing_ReturnsNonBlockingFailureAndSanitizedResult()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-upload-service-{Guid.NewGuid():N}");
        string diagnosticsRoot = Path.Combine(root, "Diagnostics");
        Directory.CreateDirectory(root);
        try
        {
            var service = new AutopilotHardwareHashUploadService(
                new StaticMediaSecretKeyReader(),
                new ThrowingTokenService(),
                new AutopilotGraphImportClient(
                    new HttpClient(new ThrowingGraphHandler())
                    {
                        BaseAddress = new Uri("https://graph.microsoft.com/", UriKind.Absolute)
                    },
                    NullLogger<AutopilotGraphImportClient>.Instance),
                NullLogger<AutopilotHardwareHashUploadService>.Instance);

            AutopilotHardwareHashUploadResult result = await service.UploadAsync(
                new AutopilotHardwareHashUploadRequest
                {
                    Settings = new DeployAutopilotHardwareHashUploadSettings
                    {
                        TenantId = "tenant-id",
                        ClientId = "client-id",
                        ActiveCertificateThumbprint = "ABCDEF123456"
                    },
                    Identity = new AutopilotHardwareHashDeviceIdentity("SER123", "HASHVALUE", null),
                    WorkspaceRootPath = root,
                    DiagnosticsRootPath = diagnosticsRoot
                },
                cancellationToken: CancellationToken.None);

            Assert.Equal(AutopilotHardwareHashUploadState.UploadFailed, result.State);
            Assert.NotNull(result.ArtifactPath);
            string json = await File.ReadAllTextAsync(result.ArtifactPath);
            Assert.DoesNotContain("access_token", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("authorization", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pfx", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("tenant-id", json, StringComparison.Ordinal);
            Assert.DoesNotContain("client-id", json, StringComparison.Ordinal);
            Assert.Contains("SER123", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UploadAsync_WhenGraphPermissionFailsAfterPfxDecrypt_ReturnsNonBlockingPermissionFailure()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-upload-service-{Guid.NewGuid():N}");
        string diagnosticsRoot = Path.Combine(root, "Diagnostics");
        Directory.CreateDirectory(root);
        byte[] mediaKey = RandomNumberGenerator.GetBytes(DeployMediaSecretEnvelopeProtector.KeySizeBytes);
        using X509Certificate2 certificate = CreateCertificate();
        const string pfxPassword = "correct-password";
        byte[] pfxBytes = certificate.Export(X509ContentType.Pfx, pfxPassword);
        try
        {
            var service = new AutopilotHardwareHashUploadService(
                new StaticMediaSecretKeyReader(mediaKey),
                new ThrowingTokenService(new HttpRequestException("Graph permission missing.", null, HttpStatusCode.Forbidden)),
                new AutopilotGraphImportClient(
                    new HttpClient(new ThrowingGraphHandler())
                    {
                        BaseAddress = new Uri("https://graph.microsoft.com/", UriKind.Absolute)
                    },
                    NullLogger<AutopilotGraphImportClient>.Instance),
                NullLogger<AutopilotHardwareHashUploadService>.Instance);

            AutopilotHardwareHashUploadResult result = await service.UploadAsync(
                new AutopilotHardwareHashUploadRequest
                {
                    Settings = new DeployAutopilotHardwareHashUploadSettings
                    {
                        TenantId = "tenant-id",
                        ClientId = "client-id",
                        ActiveCertificateThumbprint = certificate.Thumbprint,
                        CertificatePfxSecret = Encrypt(pfxBytes, mediaKey),
                        CertificatePfxPasswordSecret = Encrypt(Encoding.UTF8.GetBytes(pfxPassword), mediaKey)
                    },
                    Identity = new AutopilotHardwareHashDeviceIdentity("SER123", "HASHVALUE", null),
                    WorkspaceRootPath = root,
                    DiagnosticsRootPath = diagnosticsRoot
                },
                cancellationToken: CancellationToken.None);

            Assert.Equal(AutopilotHardwareHashUploadState.UploadFailed, result.State);
            Assert.Equal("PermissionMissing", result.FailureCode);
            Assert.NotNull(result.ArtifactPath);
            string json = await File.ReadAllTextAsync(result.ArtifactPath);
            Assert.DoesNotContain(Convert.ToBase64String(pfxBytes), json, StringComparison.Ordinal);
            Assert.DoesNotContain(pfxPassword, json, StringComparison.Ordinal);
            Assert.DoesNotContain(certificate.ExportCertificatePem(), json, StringComparison.Ordinal);
            Assert.DoesNotContain("tenant-id", json, StringComparison.Ordinal);
            Assert.DoesNotContain("client-id", json, StringComparison.Ordinal);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfxBytes);
            CryptographicOperations.ZeroMemory(mediaKey);
            Directory.Delete(root, recursive: true);
        }
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Foundry OSD Autopilot Registration",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMonths(12));
    }

    private static SecretEnvelope Encrypt(byte[] plaintext, byte[] key)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] tag = new byte[16];
        byte[] ciphertext = new byte[plaintext.Length];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

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

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed class StaticMediaSecretKeyReader(byte[]? key = null) : IMediaSecretKeyReader
    {
        public Task<byte[]> ReadAsync(string workspaceRootPath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult((key ?? new byte[DeployMediaSecretEnvelopeProtector.KeySizeBytes]).ToArray());
        }
    }

    private sealed class ThrowingTokenService(Exception? exception = null) : IAutopilotGraphTokenService
    {
        public Task<string> AcquireAccessTokenAsync(
            string tenantId,
            string clientId,
            System.Security.Cryptography.X509Certificates.X509Certificate2 certificate,
            CancellationToken cancellationToken = default)
        {
            throw exception ?? new InvalidOperationException("Token service should not be called.");
        }
    }

    private sealed class ThrowingGraphHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Graph client should not be called.");
        }
    }
}
