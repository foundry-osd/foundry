using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Catalog;

public sealed record DeploymentCatalogSnapshot(
    IReadOnlyList<OperatingSystemCatalogItem> OperatingSystems,
    IReadOnlyList<DriverPackCatalogItem> DriverPacks);
