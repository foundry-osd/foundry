using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Catalog;

public sealed class DeploymentCatalogLoadService : IDeploymentCatalogLoadService
{
    private readonly IOperatingSystemCatalogService _operatingSystemCatalogService;
    private readonly IDriverPackCatalogService _driverPackCatalogService;

    public DeploymentCatalogLoadService(
        IOperatingSystemCatalogService operatingSystemCatalogService,
        IDriverPackCatalogService driverPackCatalogService)
    {
        _operatingSystemCatalogService = operatingSystemCatalogService;
        _driverPackCatalogService = driverPackCatalogService;
    }

    public async Task<DeploymentCatalogSnapshot> LoadAsync()
    {
        Task<IReadOnlyList<OperatingSystemCatalogItem>> operatingSystemsTask = _operatingSystemCatalogService.GetCatalogAsync();
        Task<IReadOnlyList<DriverPackCatalogItem>> driverPacksTask = _driverPackCatalogService.GetCatalogAsync();

        await Task.WhenAll(operatingSystemsTask, driverPacksTask).ConfigureAwait(false);

        return new DeploymentCatalogSnapshot(
            operatingSystemsTask.Result,
            driverPacksTask.Result);
    }
}
