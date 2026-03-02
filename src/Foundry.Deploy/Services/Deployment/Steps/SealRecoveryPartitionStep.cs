using System.IO;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class SealRecoveryPartitionStep : DeploymentStepBase
{
    private readonly IWindowsDeploymentService _windowsDeploymentService;

    public SealRecoveryPartitionStep(IWindowsDeploymentService windowsDeploymentService)
    {
        _windowsDeploymentService = windowsDeploymentService;
    }

    public override int Order => 13;

    public override string Name => DeploymentStepNames.SealRecoveryPartition;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetRecoveryPartitionRoot) ||
            !context.RuntimeState.TargetRecoveryPartitionLetter.HasValue)
        {
            return DeploymentStepResult.Failed("Recovery partition is unavailable.");
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string workingDirectory = Path.Combine(targetFoundryRoot, "Temp", "Deployment");
        Directory.CreateDirectory(workingDirectory);

        await _windowsDeploymentService
            .SealRecoveryPartitionAsync(
                context.RuntimeState.TargetRecoveryPartitionRoot,
                context.RuntimeState.TargetRecoveryPartitionLetter.Value,
                workingDirectory,
                cancellationToken)
            .ConfigureAwait(false);

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"Recovery partition sealed. Recovery='{context.RuntimeState.TargetRecoveryPartitionRoot}'.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Recovery partition sealed.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetRecoveryPartitionRoot))
        {
            return DeploymentStepResult.Failed("Recovery partition is unavailable.");
        }

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Simulated recovery partition seal: {context.RuntimeState.TargetRecoveryPartitionRoot}",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(120, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Recovery partition sealed (simulation).");
    }
}
