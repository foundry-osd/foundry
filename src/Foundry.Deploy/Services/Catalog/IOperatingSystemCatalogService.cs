using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Catalog;

public interface IOperatingSystemCatalogService
{
    Task<IReadOnlyList<OperatingSystemCatalogItem>> GetCatalogAsync(CancellationToken cancellationToken = default);
}
