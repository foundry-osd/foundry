// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text.Json;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class FinalizeDeploymentAndWriteLogsStep : DeploymentStepBase
{
    public override string Name => DeploymentStepNames.FinalizeDeploymentAndWriteLogs;

    protected override Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        return ExecuteFinalizeAsync(
            context,
            "Finalizing deployment artifacts.",
            "Deployment finalized.",
            cancellationToken);
    }

    protected override Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        return ExecuteFinalizeAsync(
            context,
            "[DRY-RUN] Finalize step completed.",
            "Deployment finalized (simulation).",
            cancellationToken);
    }

    private static async Task<DeploymentStepResult> ExecuteFinalizeAsync(
        DeploymentStepExecutionContext context,
        string stepLogMessage,
        string resultMessage,
        CancellationToken cancellationToken)
    {
        context.EmitCurrentStepIndeterminate("Finalizing deployment...", "Writing completion logs...", DeploymentOperationNames.WriteLogs);
        await context.AppendLogAsync(DeploymentLogLevel.Info, stepLogMessage, cancellationToken).ConfigureAwait(false);

        context.EmitCurrentStepIndeterminate("Finalizing deployment...", "Writing deployment summary...", DeploymentOperationNames.WriteSummary);
        DeploymentArtifactHandoffResult? handoff = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
            {
                string finalRoot = DeploymentStorageLayout.FromPartitionRoot(context.RuntimeState.TargetWindowsPartitionRoot).Root;
                handoff = await context.RebindLogSessionToTargetAsync(finalRoot, cancellationToken).ConfigureAwait(false);
            }
            string summaryPath = Path.Combine(context.LogSession.StateDirectoryPath, "deployment-summary.json");
            await WriteDeploymentSummaryAsync(summaryPath, context.RuntimeState, cancellationToken).ConfigureAwait(false);
            context.RuntimeState.DeploymentSummaryPath = summaryPath;
            await context.SaveRuntimeStateAsync(cancellationToken).ConfigureAwait(false);
            if (handoff is { CanRetireSource: true, Failures.Count: 0 })
            {
                context.EmitCurrentStepIndeterminate("Finalizing deployment...", "Cleaning temporary workspace...", DeploymentOperationNames.CleanupWorkspace);
                CleanupTargetFoundryRoot(context.RuntimeState, context.LogSession);
            }
            if (handoff is { Failures.Count: > 0 })
                return DeploymentStepResult.Succeeded($"{resultMessage} Diagnostic handoff incomplete; evidence retained at '{handoff.EffectiveRootPath}'.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            await context.AppendLogAsync(DeploymentLogLevel.Warning,
                $"Final diagnostics could not be completed. Evidence retained at '{context.LogSession.RootPath}': {ex.Message}", CancellationToken.None).ConfigureAwait(false);
            return DeploymentStepResult.Succeeded($"{resultMessage} Diagnostic persistence incomplete; evidence retained at '{context.LogSession.RootPath}'.");
        }
        return DeploymentStepResult.Succeeded(resultMessage);
    }

    private static async Task WriteDeploymentSummaryAsync(
        string path,
        DeploymentRuntimeState runtimeState,
        CancellationToken cancellationToken)
    {
        string directoryPath = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Invalid deployment summary path '{path}'.");
        Directory.CreateDirectory(directoryPath);

        string json = JsonSerializer.Serialize(new
        {
            completedAtUtc = DateTimeOffset.UtcNow,
            mode = runtimeState.Mode.ToString(),
            isDryRun = runtimeState.IsDryRun,
            targetDiskNumber = runtimeState.TargetDiskNumber,
            targetComputerName = runtimeState.TargetComputerName,
            operatingSystemFileName = runtimeState.OperatingSystemFileName,
            operatingSystemUrl = runtimeState.OperatingSystemUrl,
            downloadedOperatingSystemPath = runtimeState.DownloadedOperatingSystemPath,
            downloadedDriverPackPath = runtimeState.DownloadedDriverPackPath,
            driverPackInstallMode = runtimeState.DriverPackInstallMode.ToString(),
            driverPackExtractionMethod = runtimeState.DriverPackExtractionMethod,
            extractedDriverPackPath = runtimeState.ExtractedDriverPackPath,
            deferredDriverPackagePath = runtimeState.DeferredDriverPackagePath,
            preOobeSetupCompletePath = runtimeState.PreOobeSetupCompletePath,
            preOobeRunnerPath = runtimeState.PreOobeRunnerPath,
            preOobeManifestPath = runtimeState.PreOobeManifestPath,
            preOobeScriptPaths = runtimeState.PreOobeScriptPaths,
            applyFirmwareUpdates = runtimeState.ApplyFirmwareUpdates,
            downloadedFirmwarePath = runtimeState.DownloadedFirmwarePath,
            extractedFirmwarePath = runtimeState.ExtractedFirmwarePath,
            firmwareUpdateId = runtimeState.FirmwareUpdateId,
            firmwareUpdateTitle = runtimeState.FirmwareUpdateTitle,
            autopilotEnabled = runtimeState.IsAutopilotEnabled,
            autopilotProvisioningMode = runtimeState.AutopilotProvisioningMode.ToString(),
            selectedAutopilotProfileFolderName = runtimeState.SelectedAutopilotProfileFolderName,
            selectedAutopilotProfileDisplayName = runtimeState.SelectedAutopilotProfileDisplayName,
            autopilotHardwareHashGroupTag = runtimeState.AutopilotHardwareHashGroupTag,
            autopilotHardwareHashUploadState = runtimeState.AutopilotHardwareHashUploadState.ToString(),
            autopilotHardwareHashUploadMessage = runtimeState.AutopilotHardwareHashUploadMessage,
            autopilotHardwareHashDiagnosticsPath = runtimeState.AutopilotHardwareHashDiagnosticsPath,
            targetSystemPartitionRoot = runtimeState.TargetSystemPartitionRoot,
            targetWindowsPartitionRoot = runtimeState.TargetWindowsPartitionRoot,
            targetRecoveryPartitionRoot = runtimeState.TargetRecoveryPartitionRoot,
            winReConfigured = runtimeState.WinReConfigured,
            stagedAutopilotConfigurationPath = runtimeState.StagedAutopilotConfigurationPath,
            completedSteps = runtimeState.CompletedSteps
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void CleanupTargetFoundryRoot(DeploymentRuntimeState runtimeState, DeploymentLogSession logSession)
    {
        if (string.IsNullOrWhiteSpace(runtimeState.TargetFoundryRoot) ||
            string.IsNullOrWhiteSpace(runtimeState.TargetWindowsPartitionRoot)) return;
        string legacyRoot = Path.GetFullPath(Path.Combine(runtimeState.TargetWindowsPartitionRoot, "Foundry"));
        string finalRoot = Path.GetFullPath(DeploymentStorageLayout.FromPartitionRoot(runtimeState.TargetWindowsPartitionRoot).Root);
        // Only the staging root owned by this deployment is eligible; first-boot state and payloads remain in the final root.
        if (!Path.GetFullPath(runtimeState.TargetFoundryRoot).Equals(legacyRoot, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(logSession.RootPath).Equals(finalRoot, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            FoundryDeployLogging.TryRetireRoot(legacyRoot, logSession.LogsDirectoryPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Evidence retention is safe when retirement cannot complete.
        }
    }
}
