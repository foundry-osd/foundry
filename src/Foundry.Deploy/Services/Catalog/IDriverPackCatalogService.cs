using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Catalog;

public interface IDriverPackCatalogService
{
    Task<IReadOnlyList<DriverPackCatalogItem>> GetCatalogAsync(CancellationToken cancellationToken = default);
}
