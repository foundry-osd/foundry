using System.IO;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class ConfigureTargetComputerNameStep : DeploymentStepBase
{
    private readonly IWindowsDeploymentService _windowsDeploymentService;

    public ConfigureTargetComputerNameStep(IWindowsDeploymentService windowsDeploymentService)
    {
        _windowsDeploymentService = windowsDeploymentService;
    }

    public override int Order => 8;

    public override string Name => DeploymentStepNames.ConfigureTargetComputerName;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target Windows partition is unavailable.");
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string workingDirectory = Path.Combine(targetFoundryRoot, "Temp", "Deployment");
        Directory.CreateDirectory(workingDirectory);

        context.EmitCurrentStepIndeterminate("Configuring target computer name...", "Writing offline computer name...");
        await _windowsDeploymentService
            .ConfigureOfflineComputerNameAsync(
                context.RuntimeState.TargetWindowsPartitionRoot,
                context.RuntimeState.TargetComputerName,
                context.Request.OperatingSystem.Architecture,
                workingDirectory,
                cancellationToken)
            .ConfigureAwait(false);

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"Target computer name configured: {context.RuntimeState.TargetComputerName}.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Target computer name configured.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed("Target Windows partition is unavailable.");
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string workingDirectory = Path.Combine(targetFoundryRoot, "Temp", "Deployment");
        Directory.CreateDirectory(workingDirectory);

        context.EmitCurrentStepIndeterminate("Configuring target computer name...", "Writing offline computer name...");
        await _windowsDeploymentService
            .ConfigureOfflineComputerNameAsync(
                context.RuntimeState.TargetWindowsPartitionRoot,
                context.RuntimeState.TargetComputerName,
                context.Request.OperatingSystem.Architecture,
                workingDirectory,
                cancellationToken)
            .ConfigureAwait(false);

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Simulated target computer name configuration: {context.RuntimeState.TargetComputerName}.",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(120, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Target computer name configured (simulation).");
    }
}
