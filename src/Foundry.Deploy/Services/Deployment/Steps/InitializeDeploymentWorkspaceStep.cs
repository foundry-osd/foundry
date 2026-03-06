using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class InitializeDeploymentWorkspaceStep : DeploymentStepBase
{
    public override int Order => 2;

    public override string Name => DeploymentStepNames.InitializeDeploymentWorkspace;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        context.EmitCurrentStepIndeterminate("Initializing deployment workspace...", "Creating workspace folders...");
        context.EnsureWorkspaceFolders();
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            "Workspace initialization confirmed.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Workspace initialized.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        context.EmitCurrentStepIndeterminate("Initializing deployment workspace...", "Creating workspace folders...");
        context.EnsureWorkspaceFolders();
        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            "Debug safe mode log session initialized.",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(120, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Workspace initialized (simulation).");
    }
}
