// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Foundry.Core.Services.Autopilot;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Security;
using Foundry.Deploy.Services.Deployment;
using Microsoft.Extensions.Logging.Abstractions;
using SecretEnvelope = Foundry.Core.Models.Configuration.SecretEnvelope;

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
                new FakeProfileAssignment(ProfileAssignmentOutcome.Assigned),
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
            string json = await File.ReadAllTextAsync(result.ArtifactPath, TestContext.Current.CancellationToken);
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
        byte[] mediaKey = RandomNumberGenerator.GetBytes(MediaSecretEnvelopeProtector.KeySizeBytes);
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
                new FakeProfileAssignment(ProfileAssignmentOutcome.Assigned),
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
            string json = await File.ReadAllTextAsync(result.ArtifactPath, TestContext.Current.CancellationToken);
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

    [Fact]
    public async Task UploadAsync_PassesFinalComputerNameThroughToGraph()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-upload-service-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        byte[] mediaKey = RandomNumberGenerator.GetBytes(MediaSecretEnvelopeProtector.KeySizeBytes);
        using X509Certificate2 certificate = CreateCertificate();
        const string password = "test-password";
        byte[] pfxBytes = certificate.Export(X509ContentType.Pfx, password);
        try
        {
            var handler = new SuccessfulGraphHandler();
            var service = new AutopilotHardwareHashUploadService(
                new StaticMediaSecretKeyReader(mediaKey),
                new SuccessfulTokenService(),
                new AutopilotGraphImportClient(new HttpClient(handler)
                {
                    BaseAddress = new Uri("https://graph.microsoft.com/")
                }, NullLogger<AutopilotGraphImportClient>.Instance),
                new FakeProfileAssignment(ProfileAssignmentOutcome.Assigned),
                NullLogger<AutopilotHardwareHashUploadService>.Instance);

            AutopilotHardwareHashUploadResult result = await service.UploadAsync(new AutopilotHardwareHashUploadRequest
            {
                Settings = new DeployAutopilotHardwareHashUploadSettings
                {
                    TenantId = "tenant",
                    ClientId = "client",
                    ActiveCertificateThumbprint = certificate.Thumbprint,
                    CertificatePfxSecret = Encrypt(pfxBytes, mediaKey),
                    CertificatePfxPasswordSecret = Encrypt(Encoding.UTF8.GetBytes(password), mediaKey)
                },
                Identity = new AutopilotHardwareHashDeviceIdentity("SER123", "HASH", null),
                WorkspaceRootPath = root,
                DiagnosticsRootPath = Path.Combine(root, "Diagnostics"),
                AssignedComputerName = "FINAL-PC"
            }, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.IsCompleted);
            Assert.Equal("FINAL-PC", handler.AssignedName);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfxBytes);
            CryptographicOperations.ZeroMemory(mediaKey);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UploadAsync_AfterCompletedImport_WaitsForTheProfileWithTheSameToken()
    {
        var profile = new FakeProfileAssignment(ProfileAssignmentOutcome.Assigned);
        DateTimeOffset before = DateTimeOffset.UtcNow;

        (AutopilotHardwareHashUploadResult result, string resultJson) = await RunSuccessfulUploadAsync(profile);

        Assert.Equal("token", profile.AccessToken);
        Assert.Equal("device", profile.DeviceId);
        Assert.InRange(profile.StopAtUtc!.Value, before.AddMinutes(20).AddSeconds(-5), DateTimeOffset.UtcNow.AddMinutes(20).AddSeconds(5));
        Assert.True(result.IsCompleted);
        Assert.Equal(ProfileAssignmentOutcome.Assigned, result.ProfileAssignment);
        Assert.Equal("Autopilot hardware hash imported and the Autopilot profile is assigned.", result.Message);
        Assert.Contains("\"profileAssignment\": \"Assigned\"", resultJson);
    }

    [Theory]
    [InlineData(ProfileAssignmentOutcome.TimedOut, "Autopilot hardware hash imported, but no Autopilot profile was assigned within 20 minutes.")]
    [InlineData(ProfileAssignmentOutcome.Failed, "Autopilot hardware hash imported, but Intune reported that the Autopilot profile assignment failed.")]
    [InlineData(ProfileAssignmentOutcome.Unavailable, "Autopilot hardware hash imported, but the Autopilot profile status could not be read.")]
    public async Task UploadAsync_WhenProfileIsNotAssigned_StillCompletesWithItsMessage(ProfileAssignmentOutcome outcome, string message)
    {
        (AutopilotHardwareHashUploadResult result, _) = await RunSuccessfulUploadAsync(new FakeProfileAssignment(outcome));

        Assert.True(result.IsCompleted);
        Assert.Equal(outcome, result.ProfileAssignment);
        Assert.Equal(message, result.Message);
    }

    [Fact]
    public async Task UploadAsync_WithoutAutopilotDeviceId_DoesNotWaitAndReportsUnavailable()
    {
        var profile = new FakeProfileAssignment(ProfileAssignmentOutcome.Assigned);

        (AutopilotHardwareHashUploadResult result, _) = await RunSuccessfulUploadAsync(profile, listedDeviceId: null);

        Assert.False(profile.Called);
        Assert.True(result.IsCompleted);
        Assert.Equal(ProfileAssignmentOutcome.Unavailable, result.ProfileAssignment);
        Assert.Equal("Autopilot hardware hash imported, but the Autopilot profile status could not be read.", result.Message);
    }

    [Fact]
    public async Task UploadAsync_WhenImportFails_DoesNotWait()
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-upload-service-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var profile = new FakeProfileAssignment(ProfileAssignmentOutcome.Assigned);
            var service = new AutopilotHardwareHashUploadService(
                new StaticMediaSecretKeyReader(),
                new SuccessfulTokenService(),
                new AutopilotGraphImportClient(new HttpClient(new ThrowingGraphHandler()), NullLogger<AutopilotGraphImportClient>.Instance),
                profile,
                NullLogger<AutopilotHardwareHashUploadService>.Instance);

            AutopilotHardwareHashUploadResult result = await service.UploadAsync(new AutopilotHardwareHashUploadRequest
            {
                Settings = new DeployAutopilotHardwareHashUploadSettings(),
                Identity = new AutopilotHardwareHashDeviceIdentity("SER123", "HASH", null),
                WorkspaceRootPath = root,
                DiagnosticsRootPath = Path.Combine(root, "Diagnostics")
            }, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result.IsCompleted);
            Assert.False(profile.Called);
            Assert.Null(result.ProfileAssignment);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UploadAsync_WhenCancelledDuringTheWait_Throws()
    {
        using var deployment = new CancellationTokenSource();
        var profile = new FakeProfileAssignment(ProfileAssignmentOutcome.Skipped) { OnWait = deployment.Cancel };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunSuccessfulUploadAsync(profile, cancellationToken: deployment.Token));
    }

    [Fact]
    public async Task UploadAsync_ReportsTheProfileCountdown()
    {
        var profile = new FakeProfileAssignment(ProfileAssignmentOutcome.Assigned) { WaitDuration = TimeSpan.FromMilliseconds(1500) };
        var reports = new List<AutopilotHardwareHashUploadProgress>();

        await RunSuccessfulUploadAsync(profile, progress: new SynchronousProgress(reports.Add));

        Assert.Contains(reports, report =>
            report.Message == "Waiting for the Autopilot profile assignment..." &&
            report.Detail is not null &&
            report.Detail.StartsWith("Checking the Autopilot profile (", StringComparison.Ordinal) &&
            report.Detail.EndsWith(" remaining)...", StringComparison.Ordinal));
    }

    private static async Task<(AutopilotHardwareHashUploadResult Result, string ResultJson)> RunSuccessfulUploadAsync(
        IProfileAssignmentService profile,
        string? listedDeviceId = "device",
        IProgress<AutopilotHardwareHashUploadProgress>? progress = null,
        CancellationToken? cancellationToken = null)
    {
        string root = Path.Combine(Path.GetTempPath(), $"foundry-upload-service-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        byte[] mediaKey = RandomNumberGenerator.GetBytes(MediaSecretEnvelopeProtector.KeySizeBytes);
        using X509Certificate2 certificate = CreateCertificate();
        const string password = "test-password";
        byte[] pfxBytes = certificate.Export(X509ContentType.Pfx, password);
        try
        {
            var service = new AutopilotHardwareHashUploadService(
                new StaticMediaSecretKeyReader(mediaKey),
                new SuccessfulTokenService(),
                new AutopilotGraphImportClient(new HttpClient(new SuccessfulGraphHandler { ListedDeviceId = listedDeviceId })
                {
                    BaseAddress = new Uri("https://graph.microsoft.com/")
                }, NullLogger<AutopilotGraphImportClient>.Instance),
                profile,
                NullLogger<AutopilotHardwareHashUploadService>.Instance);

            AutopilotHardwareHashUploadResult result = await service.UploadAsync(new AutopilotHardwareHashUploadRequest
            {
                Settings = new DeployAutopilotHardwareHashUploadSettings
                {
                    TenantId = "tenant",
                    ClientId = "client",
                    ActiveCertificateThumbprint = certificate.Thumbprint,
                    CertificatePfxSecret = Encrypt(pfxBytes, mediaKey),
                    CertificatePfxPasswordSecret = Encrypt(Encoding.UTF8.GetBytes(password), mediaKey)
                },
                Identity = new AutopilotHardwareHashDeviceIdentity("SER123", "HASH", null),
                WorkspaceRootPath = root,
                DiagnosticsRootPath = Path.Combine(root, "Diagnostics")
            }, progress, cancellationToken ?? TestContext.Current.CancellationToken);

            return (result, File.ReadAllText(result.ArtifactPath!));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfxBytes);
            CryptographicOperations.ZeroMemory(mediaKey);
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Returns a fixed outcome and records the arguments of the wait.</summary>
    private sealed class FakeProfileAssignment(ProfileAssignmentOutcome outcome) : IProfileAssignmentService
    {
        public bool Called { get; private set; }
        public string? AccessToken { get; private set; }
        public string? DeviceId { get; private set; }
        public DateTimeOffset? StopAtUtc { get; private set; }
        public Action? OnWait { get; init; }
        public TimeSpan WaitDuration { get; init; }

        public async Task<ProfileAssignmentOutcome> WaitAsync(string accessToken, string deviceId, DateTimeOffset stopAtUtc, CancellationToken skip)
        {
            Called = true;
            AccessToken = accessToken;
            DeviceId = deviceId;
            StopAtUtc = stopAtUtc;
            OnWait?.Invoke();
            if (WaitDuration > TimeSpan.Zero)
            {
                await Task.Delay(WaitDuration, CancellationToken.None);
            }

            return outcome;
        }
    }

    /// <summary>Reports on the calling thread, unlike <see cref="Progress{T}"/>.</summary>
    private sealed class SynchronousProgress(Action<AutopilotHardwareHashUploadProgress> report) : IProgress<AutopilotHardwareHashUploadProgress>
    {
        public void Report(AutopilotHardwareHashUploadProgress value) => report(value);
    }

    private sealed class SuccessfulTokenService : IAutopilotGraphTokenService
    {
        public Task<string> AcquireAccessTokenAsync(string tenantId, string clientId, X509Certificate2 certificate,
            CancellationToken cancellationToken = default) => Task.FromResult("token");
    }

    private sealed class SuccessfulGraphHandler : HttpMessageHandler
    {
        public string? AssignedName { get; private set; }

        /// <summary>Identifier of the listed Autopilot device; <see langword="null"/> lists the device without one.</summary>
        public string? ListedDeviceId { get; init; } = "device";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("updateDeviceProperties", StringComparison.Ordinal))
            {
                using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                AssignedName = body.RootElement.GetProperty("displayName").GetString();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            string json = request.Method == HttpMethod.Post
                ? """{ "value": [{ "id": "imported", "state": { "deviceImportStatus": "complete" } }] }"""
                : JsonSerializer.Serialize(new { value = new[] { new { id = ListedDeviceId, serialNumber = "SER123", displayName = AssignedName } } });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
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
            Kind = MediaSecretEnvelopeProtector.Kind,
            Algorithm = MediaSecretEnvelopeProtector.Algorithm,
            KeyId = MediaSecretEnvelopeProtector.DeploymentKeyId,
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

    private sealed class StaticMediaSecretKeyReader(byte[]? key = null) : IDeploymentSecretKeyProvider
    {
        public Task<byte[]> ReadAsync(string workspaceRootPath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult((key ?? new byte[MediaSecretEnvelopeProtector.KeySizeBytes]).ToArray());
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
