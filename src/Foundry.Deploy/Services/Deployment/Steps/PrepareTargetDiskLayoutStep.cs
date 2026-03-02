using System.IO;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class PrepareTargetDiskLayoutStep : DeploymentStepBase
{
    private readonly IWindowsDeploymentService _windowsDeploymentService;

    public PrepareTargetDiskLayoutStep(IWindowsDeploymentService windowsDeploymentService)
    {
        _windowsDeploymentService = windowsDeploymentService;
    }

    public override int Order => 5;

    public override string Name => DeploymentStepNames.PrepareTargetDiskLayout;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        string workingDirectory = context.ResolveWorkspaceTempPath("Deployment");
        Directory.CreateDirectory(workingDirectory);

        (_, DeploymentStepResult? validationFailure) = await context.TryGetValidatedTargetDiskAsync(cancellationToken).ConfigureAwait(false);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        DeploymentTargetLayout layout = await _windowsDeploymentService
            .PrepareTargetDiskAsync(
                context.Request.TargetDiskNumber,
                workingDirectory,
                cancellationToken)
            .ConfigureAwait(false);

        context.RuntimeState.TargetSystemPartitionRoot = layout.SystemPartitionRoot;
        context.RuntimeState.TargetWindowsPartitionRoot = layout.WindowsPartitionRoot;
        context.RuntimeState.TargetRecoveryPartitionRoot = layout.RecoveryPartitionRoot;
        context.RuntimeState.TargetRecoveryPartitionLetter = layout.RecoveryPartitionLetter;
        context.RuntimeState.TargetFoundryRoot = Path.Combine(layout.WindowsPartitionRoot, "Foundry");

        if (context.RuntimeState.Mode == DeploymentMode.Iso)
        {
            Directory.CreateDirectory(Path.Combine(context.RuntimeState.TargetFoundryRoot, "OperatingSystem"));
            Directory.CreateDirectory(Path.Combine(context.RuntimeState.TargetFoundryRoot, "DriverPack"));
        }

        if (context.LogSession is not null)
        {
            await context.RebindLogSessionToTargetAsync(context.RuntimeState.TargetFoundryRoot, cancellationToken).ConfigureAwait(false);
        }

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"Target disk prepared: system='{layout.SystemPartitionRoot}', windows='{layout.WindowsPartitionRoot}', recovery='{layout.RecoveryPartitionRoot}'.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Target disk layout prepared.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        string targetRoot = context.ResolveWorkspaceTempPath("DryRunTarget");
        string systemRoot = Path.Combine(targetRoot, "System");
        string windowsRoot = Path.Combine(targetRoot, "Windows");
        string recoveryRoot = Path.Combine(targetRoot, "Recovery");
        Directory.CreateDirectory(systemRoot);
        Directory.CreateDirectory(windowsRoot);
        Directory.CreateDirectory(recoveryRoot);

        context.RuntimeState.TargetSystemPartitionRoot = systemRoot;
        context.RuntimeState.TargetWindowsPartitionRoot = windowsRoot;
        context.RuntimeState.TargetRecoveryPartitionRoot = recoveryRoot;
        context.RuntimeState.TargetRecoveryPartitionLetter = 'R';
        context.RuntimeState.TargetFoundryRoot = Path.Combine(windowsRoot, "Foundry");

        if (context.LogSession is not null)
        {
            await context.RebindLogSessionToTargetAsync(context.RuntimeState.TargetFoundryRoot, cancellationToken).ConfigureAwait(false);
        }

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Simulated target disk layout: system='{systemRoot}', windows='{windowsRoot}', recovery='{recoveryRoot}'.",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(120, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Target disk layout prepared (simulation).");
    }
}
