// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>
/// Applies extracted INF drivers to the configured Windows recovery environment.
/// </summary>
public sealed class ApplyRecoveryDriversStep(IWindowsDeploymentService windowsDeploymentService) : DeploymentStepBase
{
    public override string Name => DeploymentStepNames.ApplyRecoveryDrivers;

    protected override Task<DeploymentStepResult> ExecuteLiveAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        return context.RuntimeState.DriverPackInstallMode switch
        {
            DriverPackInstallMode.None => Task.FromResult(DeploymentStepResult.Skipped("No driver pack operation is required.")),
            DriverPackInstallMode.DeferredSetupComplete => Task.FromResult(DeploymentStepResult.Skipped("No driver pack operation is required.")),
            DriverPackInstallMode.OfflineInf => ApplyLiveAsync(context, cancellationToken),
            _ => Task.FromResult(DeploymentStepResult.Failed("Unsupported driver pack install mode."))
        };
    }

    protected override Task<DeploymentStepResult> ExecuteDryRunAsync(
        DeploymentStepExecutionContext context,
        CancellationToken cancellationToken)
    {
        return context.RuntimeState.DriverPackInstallMode switch
        {
            DriverPackInstallMode.None => Task.FromResult(DeploymentStepResult.Skipped("No driver pack operation is required.")),
            DriverPackInstallMode.DeferredSetupComplete => Task.FromResult(DeploymentStepResult.Skipped("No driver pack operation is required.")),
            DriverPackInstallMode.OfflineInf => SimulateAsync(context, cancellationToken),
            _ => Task.FromResult(DeploymentStepResult.Failed("Unsupported driver pack install mode."))
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
        const string stepMessage = "Applying WinRE drivers...";

        IProgress<double> mountProgress = context.CreateStepPercentProgressReporter(stepMessage, "Mounting WinRE");
        IProgress<double> applyProgress = context.CreateStepPercentProgressReporter(stepMessage, "Applying WinRE drivers");
        IProgress<double> unmountProgress = context.CreateStepPercentProgressReporter(stepMessage, "Unmounting WinRE");

        await windowsDeploymentService
            .ApplyRecoveryDriversAsync(
                context.RuntimeState.TargetRecoveryPartitionRoot!,
                driverRoot,
                scratchDirectory,
                workingDirectory,
                cancellationToken,
                mountProgress,
                applyProgress,
                unmountProgress,
                onMountStarted: () => context.EmitCurrentStepIndeterminate(stepMessage, "Mounting WinRE...", DeploymentOperationNames.MountRecoveryImage),
                onApplyStarted: () => context.EmitCurrentStepIndeterminate(stepMessage, "Applying WinRE drivers...", DeploymentOperationNames.ApplyRecoveryDrivers),
                onUnmountStarted: () => context.EmitCurrentStepIndeterminate(stepMessage, "Unmounting WinRE...", DeploymentOperationNames.UnmountRecoveryImage))
            .ConfigureAwait(false);

        int infCount = Directory.EnumerateFiles(driverRoot, "*.inf", SearchOption.AllDirectories).Count();
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"Drivers applied to WinRE: {infCount} INF files from '{driverRoot}'.",
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
            $"[DRY-RUN] Simulated recovery driver apply: {infCount} INF files.",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Driver pack applied (simulation).");
    }

    private static DeploymentStepResult? Validate(DeploymentStepExecutionContext context)
    {
        if (!context.RuntimeState.WinReConfigured)
        {
            return DeploymentStepResult.Failed("Recovery partition is unavailable.");
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetRecoveryPartitionRoot))
        {
            return DeploymentStepResult.Failed("Recovery partition is unavailable.");
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.ExtractedDriverPackPath) ||
            !Directory.Exists(context.RuntimeState.ExtractedDriverPackPath))
        {
            return DeploymentStepResult.Failed("No extracted INF driver payload is available.");
        }

        return null;
    }
}
