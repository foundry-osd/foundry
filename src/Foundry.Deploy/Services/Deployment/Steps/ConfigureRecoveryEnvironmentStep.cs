using System.IO;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class ConfigureRecoveryEnvironmentStep : DeploymentStepBase
{
    private readonly IWindowsDeploymentService _windowsDeploymentService;

    public ConfigureRecoveryEnvironmentStep(IWindowsDeploymentService windowsDeploymentService)
    {
        _windowsDeploymentService = windowsDeploymentService;
    }

    public override int Order => 11;

    public override string Name => DeploymentStepNames.ConfigureRecoveryEnvironment;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot) ||
            string.IsNullOrWhiteSpace(context.RuntimeState.TargetRecoveryPartitionRoot) ||
            !context.RuntimeState.TargetRecoveryPartitionLetter.HasValue)
        {
            return DeploymentStepResult.Failed("Recovery partition is unavailable.");
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string workingDirectory = Path.Combine(targetFoundryRoot, "Temp", "Deployment");
        Directory.CreateDirectory(workingDirectory);

        await _windowsDeploymentService
            .ConfigureRecoveryEnvironmentAsync(
                context.RuntimeState.TargetWindowsPartitionRoot,
                context.RuntimeState.TargetRecoveryPartitionRoot,
                workingDirectory,
                cancellationToken)
            .ConfigureAwait(false);

        context.RuntimeState.WinReConfigured = true;
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"Recovery environment configured. Recovery='{context.RuntimeState.TargetRecoveryPartitionRoot}'.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Recovery environment configured.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetRecoveryPartitionRoot))
        {
            return DeploymentStepResult.Failed("Recovery partition is unavailable.");
        }

        context.RuntimeState.WinReConfigured = true;
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Simulated WinRE configuration for '{context.RuntimeState.TargetRecoveryPartitionRoot}'.",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Recovery environment configured (simulation).");
    }
}
