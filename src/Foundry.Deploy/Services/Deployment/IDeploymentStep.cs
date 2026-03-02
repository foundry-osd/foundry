namespace Foundry.Deploy.Services.Deployment;

public interface IDeploymentStep
{
    int Order { get; }

    string Name { get; }

    Task<DeploymentStepResult> ExecuteAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken);
}
