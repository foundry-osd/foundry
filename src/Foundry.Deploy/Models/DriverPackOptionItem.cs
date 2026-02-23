namespace Foundry.Deploy.Models;

public sealed record DriverPackOptionItem
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required DriverPackSelectionKind Kind { get; init; }
    public DriverPackCatalogItem? DriverPack { get; init; }
}
