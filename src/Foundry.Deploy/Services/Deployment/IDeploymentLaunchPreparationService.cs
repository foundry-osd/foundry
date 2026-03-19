namespace Foundry.Deploy.Services.Deployment;

public interface IDeploymentLaunchPreparationService
{
    DeploymentLaunchPreparationResult Prepare(DeploymentLaunchRequest request);
}
