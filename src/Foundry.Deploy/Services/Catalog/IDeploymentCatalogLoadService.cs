namespace Foundry.Deploy.Services.Catalog;

public interface IDeploymentCatalogLoadService
{
    Task<DeploymentCatalogSnapshot> LoadAsync();
}
