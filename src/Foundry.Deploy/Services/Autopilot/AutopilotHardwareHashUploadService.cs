// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Core.Services.Autopilot;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Security;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Decrypts media certificate material in memory, authenticates to Graph, and imports the captured hardware hash.
/// </summary>
public sealed class AutopilotHardwareHashUploadService(
    IDeploymentSecretKeyProvider deploymentSecretKeyProvider,
    IAutopilotGraphTokenService tokenService,
    AutopilotGraphImportClient graphImportClient,
    IProfileAssignmentService profileAssignment,
    ILogger<AutopilotHardwareHashUploadService> logger) : IAutopilotHardwareHashUploadService
{
    private const string UploadResultFileName = "AutopilotUploadResult.json";

    private readonly IDeploymentSecretKeyProvider deploymentSecretKeyProvider = deploymentSecretKeyProvider;
    private readonly IAutopilotGraphTokenService tokenService = tokenService;
    private readonly AutopilotGraphImportClient graphImportClient = graphImportClient;
    private readonly IProfileAssignmentService profileAssignment = profileAssignment;
    private readonly ILogger logger = logger;

    public async Task<AutopilotHardwareHashUploadResult> UploadAsync(
        AutopilotHardwareHashUploadRequest request,
        IProgress<AutopilotHardwareHashUploadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AutopilotDiagnosticsDirectory.CreateRestricted(request.DiagnosticsRootPath);

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

        byte[] deploymentKey = await deploymentSecretKeyProvider.ReadAsync(request.WorkspaceRootPath, cancellationToken)
            .ConfigureAwait(false);
        byte[]? pfxBytes = null;
        char[]? pfxPassword = null;
        try
        {
            pfxBytes = MediaSecretEnvelopeProtector.DecryptBytes(
                request.Settings.CertificatePfxSecret!,
                deploymentKey,
                MediaSecretEnvelopeProtector.DeployKeyIds);
            pfxPassword = MediaSecretEnvelopeProtector.DecryptChars(
                request.Settings.CertificatePfxPasswordSecret!,
                deploymentKey,
                MediaSecretEnvelopeProtector.DeployKeyIds);

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
            AutopilotHardwareHashUploadResult importResult = await graphImportClient.ImportHardwareHashAsync(
                new AutopilotGraphImportRequest(
                    accessToken,
                    request.Identity.SerialNumber,
                    request.Identity.HardwareHash,
                    request.Identity.GroupTag,
                    null,
                    Guid.NewGuid().ToString("D"),
                    request.AssignedComputerName),
                progress,
                cancellationToken).ConfigureAwait(false);
            if (!importResult.IsCompleted)
            {
                return importResult;
            }

            ProfileAssignmentOutcome outcome = await WaitForProfileAsync(
                accessToken,
                importResult.AutopilotDeviceId,
                progress,
                cancellationToken).ConfigureAwait(false);
            return importResult with { ProfileAssignment = outcome, Message = DescribeProfileAssignment(outcome) };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(deploymentKey);
            if (pfxBytes is not null)
            {
                CryptographicOperations.ZeroMemory(pfxBytes);
            }

            if (pfxPassword is not null)
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(pfxPassword.AsSpan()));
            }
        }
    }

    /// <summary>
    /// Waits up to <see cref="ProfileAssignmentWaiter.MaximumWait"/> for Intune to assign the Autopilot deployment profile,
    /// so the device reaches OOBE with it, and reports a countdown every second. The outcome is informational: a profile that
    /// is not assigned never fails the upload. Deployment cancellation still cancels.
    /// </summary>
    private async Task<ProfileAssignmentOutcome> WaitForProfileAsync(
        string accessToken,
        string? autopilotDeviceId,
        IProgress<AutopilotHardwareHashUploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(autopilotDeviceId))
        {
            logger.LogWarning("The Autopilot device identifier is unknown; the profile assignment wait is skipped.");
            return ProfileAssignmentOutcome.Unavailable;
        }

        DateTimeOffset stopAtUtc = DateTimeOffset.UtcNow + ProfileAssignmentWaiter.MaximumWait;
        logger.LogInformation("Waiting up to {Timeout} for the Autopilot profile assignment.", ProfileAssignmentWaiter.MaximumWait);
        Task<ProfileAssignmentOutcome> wait = profileAssignment.WaitAsync(accessToken, autopilotDeviceId, stopAtUtc, cancellationToken);
        while (!wait.IsCompleted)
        {
            TimeSpan remaining = stopAtUtc - DateTimeOffset.UtcNow;
            progress?.Report(new AutopilotHardwareHashUploadProgress(
                "Waiting for the Autopilot profile assignment...",
                $"Checking the Autopilot profile ({FormatRemaining(remaining)} remaining)..."));
            await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(1), cancellationToken)).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                // A cancelled delay completes at once: stop ticking and let the wait observe the cancellation.
                break;
            }
        }

        ProfileAssignmentOutcome outcome = await wait.ConfigureAwait(false);
        // The waiter reports a cancelled token as Skipped; here it is the deployment being cancelled.
        cancellationToken.ThrowIfCancellationRequested();
        if (outcome == ProfileAssignmentOutcome.Assigned)
        {
            logger.LogInformation("The Autopilot profile is assigned to the device.");
        }
        else
        {
            logger.LogWarning("The Autopilot profile is not assigned to the device. Outcome={Outcome}", outcome);
        }

        return outcome;
    }

    private static string DescribeProfileAssignment(ProfileAssignmentOutcome outcome) => outcome switch
    {
        ProfileAssignmentOutcome.Assigned => "Autopilot hardware hash imported and the Autopilot profile is assigned.",
        ProfileAssignmentOutcome.TimedOut => "Autopilot hardware hash imported, but no Autopilot profile was assigned within 20 minutes.",
        ProfileAssignmentOutcome.Failed => "Autopilot hardware hash imported, but Intune reported that the Autopilot profile assignment failed.",
        _ => "Autopilot hardware hash imported, but the Autopilot profile status could not be read."
    };

    // Same wording as the Graph client's visibility countdown, which the Deploy localizer already recognizes.
    private static string FormatRemaining(TimeSpan remaining)
    {
        double seconds = Math.Max(0, Math.Ceiling(remaining.TotalSeconds));
        return seconds == 1 ? "1 second" : $"{seconds:0} seconds";
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
            profileAssignment = result.ProfileAssignment?.ToString(),
            request.Identity.SerialNumber,
            request.Identity.GroupTag,
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
