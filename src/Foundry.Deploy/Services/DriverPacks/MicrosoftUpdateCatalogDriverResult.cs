namespace Foundry.Deploy.Services.DriverPacks;

public sealed record MicrosoftUpdateCatalogDriverResult
{
    public required string DestinationDirectory { get; init; }
    public int CabCount { get; init; }
    public int InfCount { get; init; }
    public required string Message { get; init; }
}
