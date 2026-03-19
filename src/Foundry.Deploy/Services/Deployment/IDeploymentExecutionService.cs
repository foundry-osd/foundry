namespace Foundry.Deploy.Services.Deployment;

public interface IDeploymentExecutionService
{
    Task<DeploymentExecutionRunResult> ExecuteAsync(DeploymentContext context);
}
