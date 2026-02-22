using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.DriverPacks;

public sealed record DriverPackSelectionResult
{
    public DriverPackCatalogItem? DriverPack { get; init; }
    public required string SelectionReason { get; init; }
}
