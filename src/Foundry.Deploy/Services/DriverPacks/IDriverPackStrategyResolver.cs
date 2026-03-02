using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.DriverPacks;

public interface IDriverPackStrategyResolver
{
    DriverPackExecutionPlan Resolve(
        DriverPackSelectionKind selectionKind,
        DriverPackCatalogItem? driverPack,
        string downloadedPath);
}
