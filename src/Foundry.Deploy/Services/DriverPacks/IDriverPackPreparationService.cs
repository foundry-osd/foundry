using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.DriverPacks;

public interface IDriverPackPreparationService
{
    Task<DriverPackPreparationResult> PrepareAsync(
        DriverPackCatalogItem driverPack,
        string archivePath,
        string extractionRootPath,
        CancellationToken cancellationToken = default);
}
