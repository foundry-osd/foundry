namespace Foundry.Deploy.Services.Deployment.Steps;

public abstract class DeploymentStepBase : IDeploymentStep
{
    public abstract int Order { get; }

    public abstract string Name { get; }

    public Task<DeploymentStepResult> ExecuteAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Request.IsDryRun
            ? ExecuteDryRunAsync(context, cancellationToken)
            : ExecuteLiveAsync(context, cancellationToken);
    }

    protected abstract Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken);

    protected abstract Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken);
}
