using System.IO;
using System.Text.Json;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>
/// Runs the selected late Autopilot provisioning path after the offline Windows image has been applied.
/// </summary>
public sealed class ProvisionAutopilotStep : DeploymentStepBase
{
    private const string TargetConfigurationRelativePath = @"Windows\Provisioning\Autopilot\AutopilotConfigurationFile.json";
    private const string HardwareHashDiagnosticsFolderName = "AutopilotHash";
    private const string HardwareHashStatusFileName = "autopilot-hash-upload-status.json";
    private const string WinPeWindowsFolderName = "Windows";

    private readonly IAutopilotHardwareHashCaptureService _hardwareHashCaptureService;

    public ProvisionAutopilotStep(IAutopilotHardwareHashCaptureService hardwareHashCaptureService)
    {
        _hardwareHashCaptureService = hardwareHashCaptureService;
    }

    public override int Order => 18;

    public override string Name => DeploymentStepNames.ProvisionAutopilot;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.IsAutopilotEnabled)
        {
            context.RuntimeState.AutopilotHardwareHashUploadState = AutopilotHardwareHashUploadState.NotPlanned;
            return DeploymentStepResult.Skipped("Autopilot is disabled.");
        }

        context.RuntimeState.AutopilotProvisioningMode = context.Request.AutopilotProvisioningMode;
        if (context.Request.AutopilotProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload)
        {
            return await PrepareHardwareHashUploadAsync(context, dryRun: false, cancellationToken).ConfigureAwait(false);
        }

        if (context.Request.SelectedAutopilotProfile is null)
        {
            return DeploymentStepResult.Failed("Autopilot is enabled but no profile was selected.");
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target Windows partition is unavailable for Autopilot staging.");
        }

        string sourceConfigurationPath = context.Request.SelectedAutopilotProfile.ConfigurationFilePath;
        if (!File.Exists(sourceConfigurationPath))
        {
            return DeploymentStepResult.Failed(
                $"Selected Autopilot profile file was not found: '{sourceConfigurationPath}'.");
        }

        string targetConfigurationPath = Path.Combine(
            context.RuntimeState.TargetWindowsPartitionRoot,
            TargetConfigurationRelativePath);
        string? targetDirectoryPath = Path.GetDirectoryName(targetConfigurationPath);
        if (string.IsNullOrWhiteSpace(targetDirectoryPath))
        {
            return DeploymentStepResult.Failed("Failed to resolve the target Autopilot directory.");
        }

        context.EmitCurrentStepIndeterminate("Staging Autopilot profile...", "Copying AutopilotConfigurationFile.json...");
        Directory.CreateDirectory(targetDirectoryPath);
        File.Copy(sourceConfigurationPath, targetConfigurationPath, overwrite: true);

        context.RuntimeState.StagedAutopilotConfigurationPath = targetConfigurationPath;
        context.RuntimeState.AutopilotHardwareHashUploadState = AutopilotHardwareHashUploadState.NotPlanned;
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"Autopilot profile '{context.Request.SelectedAutopilotProfile.DisplayName}' staged to '{targetConfigurationPath}'.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Autopilot profile staged.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.IsAutopilotEnabled)
        {
            context.RuntimeState.AutopilotHardwareHashUploadState = AutopilotHardwareHashUploadState.NotPlanned;
            await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped("Autopilot is disabled.");
        }

        context.RuntimeState.AutopilotProvisioningMode = context.Request.AutopilotProvisioningMode;
        if (context.Request.AutopilotProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload)
        {
            return await PrepareHardwareHashUploadAsync(context, dryRun: true, cancellationToken).ConfigureAwait(false);
        }

        if (context.Request.SelectedAutopilotProfile is null)
        {
            return DeploymentStepResult.Failed("Autopilot is enabled but no profile was selected.");
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string autopilotRoot = Path.Combine(targetFoundryRoot, "Autopilot");
        Directory.CreateDirectory(autopilotRoot);

        string manifestPath = Path.Combine(autopilotRoot, "autopilot-profile-stage.dryrun.json");
        string manifest = JsonSerializer.Serialize(new
        {
            createdAtUtc = DateTimeOffset.UtcNow,
            mode = "dry-run",
            selectedProfileFolderName = context.Request.SelectedAutopilotProfile.FolderName,
            selectedProfileDisplayName = context.Request.SelectedAutopilotProfile.DisplayName,
            sourceConfigurationFilePath = context.Request.SelectedAutopilotProfile.ConfigurationFilePath,
            targetConfigurationRelativePath = TargetConfigurationRelativePath
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        context.EmitCurrentStepIndeterminate("Staging Autopilot profile...", "Writing dry-run Autopilot manifest...");
        await File.WriteAllTextAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);
        context.RuntimeState.StagedAutopilotConfigurationPath = manifestPath;
        context.RuntimeState.AutopilotHardwareHashUploadState = AutopilotHardwareHashUploadState.NotPlanned;

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Autopilot profile staging simulated: {manifestPath}",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Autopilot profile staged (simulation).");
    }

    private async Task<DeploymentStepResult> PrepareHardwareHashUploadAsync(
        DeploymentStepExecutionContext context,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        DeployAutopilotHardwareHashUploadSettings settings = context.Request.AutopilotHardwareHashUpload;
        context.RuntimeState.AutopilotHardwareHashGroupTag = NormalizeGroupTag(settings.DefaultGroupTag);
        context.RuntimeState.StagedAutopilotConfigurationPath = null;
        context.RuntimeState.AutopilotHardwareHashUploadState = AutopilotHardwareHashUploadState.Planned;

        if (!dryRun && string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target Windows partition is unavailable for Autopilot hardware hash upload.");
        }

        if (settings.ActiveCertificateExpiresOnUtc is DateTimeOffset expiresOn &&
            expiresOn <= DateTimeOffset.UtcNow)
        {
            const string message = "Autopilot hardware hash upload skipped because the embedded certificate is expired.";
            await WriteHardwareHashStatusAsync(
                context,
                AutopilotHardwareHashUploadState.SkippedCertificateExpired,
                message,
                cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped(message);
        }

        if (!HasRequiredHardwareHashMetadata(settings))
        {
            const string message = "Autopilot hardware hash upload skipped because media metadata is incomplete.";
            await WriteHardwareHashStatusAsync(
                context,
                AutopilotHardwareHashUploadState.SkippedMissingConfiguration,
                message,
                cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped(message);
        }

        if (dryRun)
        {
            return await WriteDryRunHardwareHashManifestAsync(context, cancellationToken).ConfigureAwait(false);
        }

        string diagnosticsPath = ResolveHardwareHashDiagnosticsPath(context);
        context.EmitCurrentStepIndeterminate("Capturing Autopilot hardware hash...", "Running OA3Tool...");
        AutopilotHardwareHashCaptureResult captureResult = await _hardwareHashCaptureService
            .CaptureAsync(
                new AutopilotHardwareHashCaptureRequest
                {
                    TargetWindowsRootPath = context.RuntimeState.TargetWindowsPartitionRoot!,
                    WinPeWindowsRootPath = ResolveWinPeWindowsRoot(context.RuntimeState.WorkspaceRoot),
                    WorkspaceRootPath = context.RuntimeState.WorkspaceRoot,
                    DiagnosticsRootPath = diagnosticsPath,
                    GroupTag = context.RuntimeState.AutopilotHardwareHashGroupTag
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!captureResult.IsSuccess)
        {
            string failureMessage = $"Autopilot hardware hash capture failed: {captureResult.Message}";
            await WriteHardwareHashStatusAsync(
                context,
                AutopilotHardwareHashUploadState.CaptureFailed,
                failureMessage,
                cancellationToken,
                captureResult.FailureCode.ToString()).ConfigureAwait(false);
            return DeploymentStepResult.Failed(failureMessage);
        }

        string capturedMessage = "Autopilot hardware hash captured. Graph upload will run in a later phase.";
        await WriteHardwareHashStatusAsync(
            context,
            AutopilotHardwareHashUploadState.HashCaptured,
            capturedMessage,
            cancellationToken,
            failureCode: null,
            captureResult).ConfigureAwait(false);
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"Autopilot hardware hash captured. DiagnosticsPath='{context.RuntimeState.AutopilotHardwareHashDiagnosticsPath}', GroupTag='{FormatGroupTagForLog(context.RuntimeState.AutopilotHardwareHashGroupTag)}'.",
            cancellationToken).ConfigureAwait(false);
        return DeploymentStepResult.Succeeded("Autopilot hardware hash captured.");
    }

    private static async Task<DeploymentStepResult> WriteDryRunHardwareHashManifestAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        string hashUploadTargetFoundryRoot = context.EnsureTargetFoundryRoot();
        string hashUploadAutopilotRoot = Path.Combine(hashUploadTargetFoundryRoot, "Autopilot");
        Directory.CreateDirectory(hashUploadAutopilotRoot);

        string hashUploadManifestPath = Path.Combine(hashUploadAutopilotRoot, "autopilot-hash-upload.dryrun.json");
        string hashUploadManifest = JsonSerializer.Serialize(new
        {
            createdAtUtc = DateTimeOffset.UtcNow,
            mode = "dry-run",
            provisioningMode = "hardwareHashUpload",
            tenantId = context.Request.AutopilotHardwareHashUpload.TenantId,
            clientId = context.Request.AutopilotHardwareHashUpload.ClientId,
            activeCertificateThumbprint = context.Request.AutopilotHardwareHashUpload.ActiveCertificateThumbprint,
            activeCertificateExpiresOnUtc = context.Request.AutopilotHardwareHashUpload.ActiveCertificateExpiresOnUtc,
            groupTag = context.RuntimeState.AutopilotHardwareHashGroupTag,
            uploadState = AutopilotHardwareHashUploadState.DryRunPrepared.ToString()
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        context.EmitCurrentStepIndeterminate("Preparing Autopilot hardware hash upload...", "Writing dry-run Autopilot hash manifest...");
        await File.WriteAllTextAsync(hashUploadManifestPath, hashUploadManifest, cancellationToken).ConfigureAwait(false);
        context.RuntimeState.StagedAutopilotConfigurationPath = hashUploadManifestPath;
        await WriteHardwareHashStatusAsync(
            context,
            AutopilotHardwareHashUploadState.DryRunPrepared,
            "Autopilot hardware hash upload prepared for dry run.",
            cancellationToken).ConfigureAwait(false);

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Autopilot hardware hash upload simulated: {hashUploadManifestPath}",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Autopilot hardware hash upload prepared (simulation).");
    }

    private static async Task WriteHardwareHashStatusAsync(
        DeploymentStepExecutionContext context,
        AutopilotHardwareHashUploadState state,
        string message,
        CancellationToken cancellationToken,
        string? failureCode = null,
        AutopilotHardwareHashCaptureResult? captureResult = null)
    {
        string diagnosticsPath = ResolveHardwareHashDiagnosticsPath(context);
        Directory.CreateDirectory(diagnosticsPath);
        context.RuntimeState.AutopilotHardwareHashUploadState = state;
        context.RuntimeState.AutopilotHardwareHashUploadMessage = message;
        context.RuntimeState.AutopilotHardwareHashDiagnosticsPath = diagnosticsPath;

        string statusPath = Path.Combine(diagnosticsPath, HardwareHashStatusFileName);
        string statusJson = JsonSerializer.Serialize(new
        {
            createdAtUtc = DateTimeOffset.UtcNow,
            provisioningMode = "hardwareHashUpload",
            uploadState = state.ToString(),
            message,
            failureCode,
            groupTag = context.RuntimeState.AutopilotHardwareHashGroupTag,
            serialNumber = captureResult?.Identity?.SerialNumber,
            oa3XmlPath = captureResult?.Oa3XmlPath,
            oa3LogPath = captureResult?.Oa3LogPath,
            autopilotHwidCsvPath = captureResult?.CsvPath,
            tenantId = context.Request.AutopilotHardwareHashUpload.TenantId,
            clientId = context.Request.AutopilotHardwareHashUpload.ClientId,
            activeCertificateThumbprint = context.Request.AutopilotHardwareHashUpload.ActiveCertificateThumbprint,
            activeCertificateExpiresOnUtc = context.Request.AutopilotHardwareHashUpload.ActiveCertificateExpiresOnUtc
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(statusPath, statusJson, cancellationToken).ConfigureAwait(false);
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"Autopilot hardware hash upload state: {state}. DiagnosticsPath='{diagnosticsPath}'.",
            cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveHardwareHashDiagnosticsPath(DeploymentStepExecutionContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return Path.Combine(
                context.RuntimeState.TargetWindowsPartitionRoot,
                "Windows",
                "Temp",
                "Foundry",
                "Logs",
                HardwareHashDiagnosticsFolderName);
        }

        return Path.Combine(context.EnsureTargetFoundryRoot(), "Logs", HardwareHashDiagnosticsFolderName);
    }

    private static string ResolveWinPeWindowsRoot(string workspaceRoot)
    {
        string fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        string? driveRoot = Path.GetPathRoot(fullWorkspaceRoot);
        return string.IsNullOrWhiteSpace(driveRoot)
            ? Path.Combine(fullWorkspaceRoot, "..", WinPeWindowsFolderName)
            : Path.Combine(driveRoot, WinPeWindowsFolderName);
    }

    private static bool HasRequiredHardwareHashMetadata(DeployAutopilotHardwareHashUploadSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.TenantId) &&
               !string.IsNullOrWhiteSpace(settings.ClientId) &&
               !string.IsNullOrWhiteSpace(settings.ActiveCertificateKeyId) &&
               !string.IsNullOrWhiteSpace(settings.ActiveCertificateThumbprint) &&
               settings.ActiveCertificateExpiresOnUtc is not null;
    }

    private static string? NormalizeGroupTag(string? groupTag)
    {
        return string.IsNullOrWhiteSpace(groupTag)
            ? null
            : groupTag.Trim();
    }

    private static string FormatGroupTagForLog(string? groupTag)
    {
        return string.IsNullOrWhiteSpace(groupTag)
            ? "None"
            : groupTag;
    }
}
