using System.IO;
using System.Text.Json;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class FinalizeDeploymentAndWriteLogsStep : DeploymentStepBase
{
    public override int Order => 14;

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
        await context.AppendLogAsync(DeploymentLogLevel.Info, stepLogMessage, cancellationToken).ConfigureAwait(false);
        await context.AppendLogAsync(DeploymentLogLevel.Info, "[SUCCESS] Deployment orchestration completed.", cancellationToken).ConfigureAwait(false);

        string summaryPath = await PersistFinalArtifactsAsync(context, cancellationToken).ConfigureAwait(false);
        context.RuntimeState.DeploymentSummaryPath = summaryPath;

        CleanupTargetFoundryRoot(context.RuntimeState, context.LogSession);
        return DeploymentStepResult.Succeeded(resultMessage);
    }

    private static async Task<string> PersistFinalArtifactsAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            string transientRoot = context.RuntimeState.ResolvedCache?.RootPath
                ?? throw new InvalidOperationException("Cache strategy has not been resolved.");
            string summaryPath = Path.Combine(transientRoot, "State", "deployment-summary.json");
            await WriteDeploymentSummaryAsync(summaryPath, context.RuntimeState, cancellationToken).ConfigureAwait(false);
            return summaryPath;
        }

        string targetWindowsTempRoot = Path.Combine(context.RuntimeState.TargetWindowsPartitionRoot, "Windows", "Temp", "Foundry");
        Directory.CreateDirectory(targetWindowsTempRoot);

        CopyDirectoryContents(context.LogSession.LogsDirectoryPath, Path.Combine(targetWindowsTempRoot, "Logs"));
        CopyDirectoryContents(context.LogSession.StateDirectoryPath, Path.Combine(targetWindowsTempRoot, "State"));

        string finalSummaryPath = Path.Combine(targetWindowsTempRoot, "deployment-summary.json");
        await WriteDeploymentSummaryAsync(finalSummaryPath, context.RuntimeState, cancellationToken).ConfigureAwait(false);
        return finalSummaryPath;
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
            preparedDriverPath = runtimeState.PreparedDriverPath,
            targetSystemPartitionRoot = runtimeState.TargetSystemPartitionRoot,
            targetWindowsPartitionRoot = runtimeState.TargetWindowsPartitionRoot,
            targetRecoveryPartitionRoot = runtimeState.TargetRecoveryPartitionRoot,
            winReConfigured = runtimeState.WinReConfigured,
            autopilotWorkflowPath = runtimeState.AutopilotWorkflowPath,
            completedSteps = runtimeState.CompletedSteps
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }

    private static void CleanupTargetFoundryRoot(DeploymentRuntimeState runtimeState, DeploymentLogSession? logSession)
    {
        if (string.IsNullOrWhiteSpace(runtimeState.TargetFoundryRoot) ||
            string.IsNullOrWhiteSpace(runtimeState.TargetWindowsPartitionRoot))
        {
            return;
        }

        string finalRoot = Path.Combine(runtimeState.TargetWindowsPartitionRoot, "Windows", "Temp", "Foundry");
        if (runtimeState.TargetFoundryRoot.Equals(finalRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (logSession is not null &&
            logSession.RootPath.Equals(runtimeState.TargetFoundryRoot, StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteDirectory(runtimeState.TargetFoundryRoot);
            return;
        }

        TryDeleteDirectory(runtimeState.TargetFoundryRoot);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) ||
            !Directory.Exists(sourceDirectory) ||
            sourceDirectory.Equals(destinationDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(destinationDirectory);
        foreach (string sourceFilePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, sourceFilePath);
            string destinationPath = Path.Combine(destinationDirectory, relativePath);
            string? destinationFolder = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationFolder))
            {
                Directory.CreateDirectory(destinationFolder);
            }

            File.Copy(sourceFilePath, destinationPath, overwrite: true);
        }
    }
}
