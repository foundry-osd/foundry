// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Logging;
using Foundry.Utilities.IO;
using Foundry.Utilities.Progress;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>Copies a deferred driver installer to retained Windows storage before setup tasks are assembled.</summary>
public sealed class StageDriverInstallerStep(IDriverPackStrategyResolver driverPackStrategyResolver) : DeploymentStepBase
{
    private const int FileCopyBufferSize = 80 * 1024;

    public override string Name => DeploymentStepNames.StageDriverInstaller;

    protected override Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken) =>
        StageAsync(context, false, cancellationToken);

    protected override Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken) =>
        StageAsync(context, true, cancellationToken);

    private async Task<DeploymentStepResult> StageAsync(
        DeploymentStepExecutionContext context,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.RuntimeState.DriverPackInstallMode != DriverPackInstallMode.DeferredSetupComplete)
        {
            return DeploymentStepResult.Skipped("No deferred driver installer is required.");
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target Windows partition is unavailable.",
                DeploymentFailure.Guard(DeploymentOperationNames.StageDeferredDriverPack,
                    DeploymentFailureReasons.MissingResource, "missing_target_partition"));
        }

        string sourcePath = context.RuntimeState.DownloadedDriverPackPath ?? string.Empty;
        if (!File.Exists(sourcePath))
        {
            return DeploymentStepResult.Failed("Driver pack source payload is unavailable for deferred staging.",
                DeploymentFailure.Guard(DeploymentOperationNames.StageDeferredDriverPack,
                    DeploymentFailureReasons.MissingResource, "missing_driver_payload"));
        }

        DriverPackExecutionPlan plan = driverPackStrategyResolver.Resolve(
            context.Request.DriverPackSelectionKind, context.Request.DriverPack, sourcePath);
        if (plan.DeferredCommandKind == DeferredDriverPackageCommandKind.None)
        {
            return DeploymentStepResult.Failed("Deferred driver pack staging was requested without a supported deferred command.",
                DeploymentFailure.Guard(DeploymentOperationNames.StageDeferredDriverPack,
                    DeploymentFailureReasons.InvalidInput, "unsupported_deferred_driver_command"));
        }

        string targetPath = Path.Combine(context.RuntimeState.TargetWindowsPartitionRoot,
            "Windows", "Temp", "Foundry", "DriverPack", "Packages", Path.GetFileName(sourcePath));
        context.EmitCurrentStepIndeterminate("Staging driver installer...", "Copying driver package...", DeploymentOperationNames.StageDeferredDriverPack);
        if (!dryRun)
        {
            IProgress<double> progress = context.CreateStepPercentProgressReporter("Staging driver installer...", "Copying package");
            await CopyFileWithProgressAsync(sourcePath, targetPath, progress, cancellationToken).ConfigureAwait(false);
        }

        context.RuntimeState.DeferredDriverPackagePath = targetPath;
        string message = dryRun ? "Driver installer staged (simulation)." : "Driver installer staged.";
        await context.AppendLogAsync(DeploymentLogLevel.Info, message, cancellationToken).ConfigureAwait(false);
        return DeploymentStepResult.Succeeded(message);
    }

    private static async Task CopyFileWithProgressAsync(
        string sourcePath,
        string destinationPath,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        long totalBytes = new FileInfo(sourcePath).Length;
        progress.Report(0d);
        await using FileStream sourceStream = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileCopyBufferSize, useAsync: true);
        await using FileStream destinationStream = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, FileCopyBufferSize, useAsync: true);
        await StreamCopy.CopyAsync(sourceStream, destinationStream, copiedBytes =>
        {
            double? percentage = TransferProgress.CalculatePercentage(copiedBytes, totalBytes);
            if (percentage.HasValue) progress.Report(percentage.Value);
        }, cancellationToken).ConfigureAwait(false);
        progress.Report(100d);
    }
}
