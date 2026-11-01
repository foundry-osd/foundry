// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text.Json;
using Foundry.Core.Services.Autopilot;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class FinalizeDeploymentAndWriteLogsStep(
    INetworkCachePublisher? networkCachePublisher = null,
    INetworkCacheSessionAccessor? networkCacheAccessor = null) : DeploymentStepBase
{
    private static readonly TimeSpan NetworkCacheDrainLimit = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan UploadProgressInterval = TimeSpan.FromMilliseconds(100);

    public override string Name => DeploymentStepNames.FinalizeDeploymentAndWriteLogs;

    /// <summary>Gets the time queued network cache uploads get before the workspace is cleaned; two minutes outside tests.</summary>
    internal TimeSpan DrainLimit { get; init; } = NetworkCacheDrainLimit;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        await CompleteNetworkCacheAsync(context, cancellationToken).ConfigureAwait(false);
        return await ExecuteFinalizeAsync(
            context,
            "Finalizing deployment artifacts.",
            "Deployment finalized.",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gives queued network cache uploads a bounded time before the target workspace, which holds the files being
    /// published, is cleaned; then closes the share session.
    /// </summary>
    private async Task CompleteNetworkCacheAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (networkCachePublisher is not null)
        {
            context.EmitCurrentStepIndeterminate("Finalizing deployment...", "Writing completion logs...", DeploymentOperationNames.WriteLogs);
            DateTimeOffset deadline = DateTimeOffset.UtcNow + DrainLimit;
            Task drain = networkCachePublisher.DrainAsync(DrainLimit, cancellationToken);
            // While an upload is still running, the step says so with its progress and the time left, like the Autopilot upload.
            // Running out of time is not shown: the next device downloads the file again, and the log records it. Once the
            // deadline passes, the publisher only waits a short grace period for the abandoned upload, so the step goes back
            // to its own status instead of showing a countdown at zero.
            bool showsUpload = false;
            while (!drain.IsCompleted)
            {
                TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining > TimeSpan.Zero && networkCachePublisher.CurrentUpload is { } upload)
                {
                    context.EmitCurrentStep(DeploymentStepState.Running, "Finishing the copy to the network cache...",
                        Percent(upload), stepSubProgressIndeterminate: upload.TotalBytes <= 0, FormatUpload(upload, remaining));
                    showsUpload = true;
                }
                else if (showsUpload && remaining <= TimeSpan.Zero)
                {
                    context.EmitCurrentStepIndeterminate("Finalizing deployment...", "Writing completion logs...", DeploymentOperationNames.WriteLogs);
                    showsUpload = false;
                }

                await Task.WhenAny(drain, Task.Delay(UploadProgressInterval, cancellationToken)).ConfigureAwait(false);
            }

            await drain.ConfigureAwait(false);
        }

        networkCacheAccessor?.Current?.Dispose();
    }

    private static double? Percent(NetworkCacheUploadStatus upload) =>
        upload.TotalBytes > 0 ? Math.Min(100d, upload.BytesCopied * 100d / upload.TotalBytes) : null;

    /// <summary>
    /// Formats the invariant upload line: progress of the current file, files still waiting, and the time left before Deploy
    /// moves on. <see cref="Foundry.Deploy.Services.Localization.DeploymentUiTextLocalizer"/> parses this exact shape to show it in the UI language.
    /// </summary>
    internal static string FormatUpload(NetworkCacheUploadStatus upload, TimeSpan remaining)
    {
        double seconds = Math.Max(0, Math.Ceiling(remaining.TotalSeconds));
        string timeLeft = seconds == 1 ? "1 second" : $"{seconds:0} seconds";
        string progress = upload.TotalBytes > 0
            ? $"{Percent(upload):0.#}% ({DeploymentStepExecutionContext.FormatByteSize(upload.BytesCopied)} / {DeploymentStepExecutionContext.FormatByteSize(upload.TotalBytes)})"
            : DeploymentStepExecutionContext.FormatByteSize(upload.BytesCopied);
        string waiting = upload.FilesWaiting > 0 ? $", {upload.FilesWaiting} more" : string.Empty;
        return $"{upload.FileName}: {progress}{waiting} ({timeLeft} remaining)";
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
            preOobeRunnerPath = runtimeState.PreOobeRunnerPath,
            preOobeManifestPath = runtimeState.PreOobeManifestPath,
            domainJoinStatus = runtimeState.DomainJoinStatus,
            domainJoinSkipCode = runtimeState.DomainJoinSkipCode,
            actualWindowsEditionId = runtimeState.ActualWindowsEditionId,
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
            completedSteps = runtimeState.CompletedSteps.Append(DeploymentStepNames.FinalizeDeploymentAndWriteLogs),
            stepOutcomes = runtimeState.StepOutcomes.Append(new DeploymentStepOutcome(
                DeploymentStepNames.FinalizeDeploymentAndWriteLogs, DeploymentStepState.Succeeded,
                runtimeState.IsDryRun ? "Deployment finalized (simulation)." : "Deployment finalized."))
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
