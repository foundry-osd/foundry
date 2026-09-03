// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>
/// Applies extracted INF drivers to the offline Windows image.
/// </summary>
public sealed class ApplyDriverPackStep(IWindowsDeploymentService windowsDeploymentService) : DeploymentStepBase
{
    public override string Name => DeploymentStepNames.ApplyDriverPack;

    protected override Task<DeploymentStepResult> ExecuteLiveAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        return context.RuntimeState.DriverPackInstallMode switch
        {
            DriverPackInstallMode.None => Task.FromResult(DeploymentStepResult.Skipped("No driver pack operation is required.")),
            DriverPackInstallMode.OfflineInf => ApplyLiveAsync(context, cancellationToken),
            DriverPackInstallMode.DeferredSetupComplete => Task.FromResult(DeploymentStepResult.Skipped("Driver pack prepared for deferred installation.")),
            _ => Task.FromResult(CreateUnsupportedModeFailure())
        };
    }

    protected override Task<DeploymentStepResult> ExecuteDryRunAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        return context.RuntimeState.DriverPackInstallMode switch
        {
            DriverPackInstallMode.None => Task.FromResult(DeploymentStepResult.Skipped("No driver pack operation is required.")),
            DriverPackInstallMode.OfflineInf => SimulateAsync(context, cancellationToken),
            DriverPackInstallMode.DeferredSetupComplete => Task.FromResult(DeploymentStepResult.Skipped("Driver pack prepared for deferred installation.")),
            _ => Task.FromResult(CreateUnsupportedModeFailure())
        };
    }

    private async Task<DeploymentStepResult> ApplyLiveAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        DeploymentStepResult? validationFailure = Validate(context);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        string driverRoot = context.RuntimeState.ExtractedDriverPackPath!;
        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string workingDirectory = Path.Combine(targetFoundryRoot, "Temp", "Deployment");
        string scratchDirectory = Path.Combine(targetFoundryRoot, "Temp", "Dism");
        const string stepMessage = "Applying driver pack...";

        context.EmitCurrentStepIndeterminate(stepMessage, "Applying Windows drivers...", DeploymentOperationNames.ApplyDriverPack);
        IProgress<double> progress = context.CreateStepPercentProgressReporter(stepMessage, "Applying Windows drivers");
        await windowsDeploymentService
            .ApplyOfflineDriversAsync(
                context.RuntimeState.TargetWindowsPartitionRoot!,
                driverRoot,
                scratchDirectory,
                workingDirectory,
                cancellationToken,
                progress)
            .ConfigureAwait(false);

        int infCount = Directory.EnumerateFiles(driverRoot, "*.inf", SearchOption.AllDirectories).Count();
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"Driver pack applied offline to Windows: {infCount} INF files from '{driverRoot}'.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Driver pack applied.");
    }

    private static async Task<DeploymentStepResult> SimulateAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        DeploymentStepResult? validationFailure = Validate(context);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        int infCount = Directory.EnumerateFiles(
            context.RuntimeState.ExtractedDriverPackPath!,
            "*.inf",
            SearchOption.AllDirectories).Count();
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Simulated offline driver pack apply to Windows: {infCount} INF files.",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Driver pack applied (simulation).");
    }

    private static DeploymentStepResult? Validate(DeploymentStepExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.ExtractedDriverPackPath) ||
            !Directory.Exists(context.RuntimeState.ExtractedDriverPackPath))
        {
            return DeploymentStepResult.Failed(
                "No extracted INF driver payload is available.",
                DeploymentFailure.Guard(
                    DeploymentOperationNames.ApplyDriverPack,
                    DeploymentFailureReasons.MissingResource,
                    "missing_driver_payload"));
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed(
                "Target Windows partition is unavailable.",
                DeploymentFailure.Guard(
                    DeploymentOperationNames.ApplyDriverPack,
                    DeploymentFailureReasons.MissingResource,
                    "missing_target_partition"));
        }

        return null;
    }

    private static DeploymentStepResult CreateUnsupportedModeFailure() =>
        DeploymentStepResult.Failed(
            "Unsupported driver pack install mode.",
            DeploymentFailure.Guard(
                DeploymentOperationNames.ApplyDriverPack,
                DeploymentFailureReasons.InvalidInput,
                "unsupported_driver_mode"));
}
