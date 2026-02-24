namespace Foundry.Deploy.Services.DriverPacks;

public sealed record MicrosoftUpdateCatalogDriverResult
{
    public required string DestinationDirectory { get; init; }
    public required int InfCount { get; init; }
    public required string Message { get; init; }
}
