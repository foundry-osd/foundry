using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.DriverPacks;

public interface IDriverPackSelectionService
{
    DriverPackSelectionResult SelectBest(
        IReadOnlyList<DriverPackCatalogItem> catalog,
        HardwareProfile hardware,
        OperatingSystemCatalogItem operatingSystem);
}
