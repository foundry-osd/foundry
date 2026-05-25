using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Deploy.Services.Deployment;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Decrypts media certificate material in memory, authenticates to Graph, and imports the captured hardware hash.
/// </summary>
public sealed class AutopilotHardwareHashUploadService(
    IMediaSecretKeyReader mediaSecretKeyReader,
    IAutopilotGraphTokenService tokenService,
    AutopilotGraphImportClient graphImportClient,
    ILogger<AutopilotHardwareHashUploadService> logger) : IAutopilotHardwareHashUploadService
{
    private const string UploadResultFileName = "AutopilotUploadResult.json";

    private readonly IMediaSecretKeyReader mediaSecretKeyReader = mediaSecretKeyReader;
    private readonly IAutopilotGraphTokenService tokenService = tokenService;
    private readonly AutopilotGraphImportClient graphImportClient = graphImportClient;
    private readonly ILogger logger = logger;

    public async Task<AutopilotHardwareHashUploadResult> UploadAsync(
        AutopilotHardwareHashUploadRequest request,
        IProgress<AutopilotHardwareHashUploadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Directory.CreateDirectory(request.DiagnosticsRootPath);

        AutopilotHardwareHashUploadResult result;
        try
        {
            result = await UploadCoreAsync(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FileNotFoundException or CryptographicException or HttpRequestException or JsonException)
        {
            logger.LogWarning(ex, "Autopilot hardware hash upload failed and OS deployment will continue.");
            result = AutopilotHardwareHashUploadResult.Failed(
                AutopilotHardwareHashUploadState.UploadFailed,
                $"Autopilot hardware hash upload skipped: {ex.Message}",
                ResolveFailureCode(ex));
        }

        return await WriteSanitizedResultAsync(request, result, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AutopilotHardwareHashUploadResult> UploadCoreAsync(
        AutopilotHardwareHashUploadRequest request,
        IProgress<AutopilotHardwareHashUploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        progress?.Report(new AutopilotHardwareHashUploadProgress(
            "Preparing Autopilot hardware hash upload...",
            "Decrypting media certificate..."));

        byte[] mediaKey = await mediaSecretKeyReader.ReadAsync(request.WorkspaceRootPath, cancellationToken)
            .ConfigureAwait(false);
        byte[]? pfxBytes = null;
        try
        {
            pfxBytes = DeployMediaSecretEnvelopeProtector.DecryptBytes(
                request.Settings.CertificatePfxSecret!,
                mediaKey);
            string pfxPassword = DeployMediaSecretEnvelopeProtector.DecryptString(
                request.Settings.CertificatePfxPasswordSecret!,
                mediaKey);

            progress?.Report(new AutopilotHardwareHashUploadProgress(
                "Authenticating Autopilot hardware hash upload...",
                "Requesting Microsoft Graph token..."));
            using X509Certificate2 certificate = AutopilotCertificateCredential.Load(
                pfxBytes,
                pfxPassword,
                request.Settings.ActiveCertificateThumbprint!);
            string accessToken = await tokenService.AcquireAccessTokenAsync(
                request.Settings.TenantId!,
                request.Settings.ClientId!,
                certificate,
                cancellationToken).ConfigureAwait(false);

            progress?.Report(new AutopilotHardwareHashUploadProgress(
                "Uploading Autopilot hardware hash...",
                "Importing hardware hash into Microsoft Graph..."));
            return await graphImportClient.ImportHardwareHashAsync(
                new AutopilotGraphImportRequest(
                    accessToken,
                    request.Identity.SerialNumber,
                    request.Identity.HardwareHash,
                    request.Identity.GroupTag,
                    null,
                    Guid.NewGuid().ToString("D")),
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(mediaKey);
            if (pfxBytes is not null)
            {
                CryptographicOperations.ZeroMemory(pfxBytes);
            }
        }
    }

    private static void ValidateRequest(AutopilotHardwareHashUploadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Settings.TenantId))
        {
            throw new InvalidOperationException("Tenant ID is missing from the Autopilot upload configuration.");
        }

        if (string.IsNullOrWhiteSpace(request.Settings.ClientId))
        {
            throw new InvalidOperationException("Client ID is missing from the Autopilot upload configuration.");
        }

        if (string.IsNullOrWhiteSpace(request.Settings.ActiveCertificateThumbprint))
        {
            throw new InvalidOperationException("Certificate thumbprint is missing from the Autopilot upload configuration.");
        }

        if (request.Settings.CertificatePfxSecret is null || request.Settings.CertificatePfxPasswordSecret is null)
        {
            throw new InvalidOperationException("Encrypted certificate material is missing from the Autopilot upload configuration.");
        }

        if (string.IsNullOrWhiteSpace(request.Identity.SerialNumber))
        {
            throw new InvalidOperationException("Captured Autopilot serial number is empty.");
        }

        if (string.IsNullOrWhiteSpace(request.Identity.HardwareHash))
        {
            throw new InvalidOperationException("Captured Autopilot hardware hash is empty.");
        }
    }

    private static string ResolveFailureCode(Exception exception)
    {
        return exception switch
        {
            FileNotFoundException => "CertificateMissing",
            CryptographicException => "AuthenticationFailed",
            HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } => "AuthenticationFailed",
            HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => "PermissionMissing",
            HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "GraphUnavailable",
            HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError } => "IntuneUnavailable",
            HttpRequestException => "GraphUnavailable",
            _ => "UploadFailed"
        };
    }

    private static async Task<AutopilotHardwareHashUploadResult> WriteSanitizedResultAsync(
        AutopilotHardwareHashUploadRequest request,
        AutopilotHardwareHashUploadResult result,
        CancellationToken cancellationToken)
    {
        string resultPath = Path.Combine(request.DiagnosticsRootPath, UploadResultFileName);
        string json = JsonSerializer.Serialize(new
        {
            createdAtUtc = DateTimeOffset.UtcNow,
            result.State,
            result.Message,
            result.FailureCode,
            result.ImportId,
            result.ImportedIdentityId,
            result.AutopilotDeviceId,
            request.Identity.SerialNumber,
            request.Identity.GroupTag,
            tenantId = request.Settings.TenantId,
            clientId = request.Settings.ClientId,
            activeCertificateThumbprint = request.Settings.ActiveCertificateThumbprint,
            activeCertificateExpiresOnUtc = request.Settings.ActiveCertificateExpiresOnUtc
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        await File.WriteAllTextAsync(resultPath, json, cancellationToken).ConfigureAwait(false);
        return result with { ArtifactPath = resultPath };
    }
}
