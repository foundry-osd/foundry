namespace Foundry.Deploy.Services.Startup;

public interface IDeploymentStartupCoordinator
{
    Task<DeploymentStartupSnapshot> InitializeAsync(DeploymentStartupRequest request);
}
