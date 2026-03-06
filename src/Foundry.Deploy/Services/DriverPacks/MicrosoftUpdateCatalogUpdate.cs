namespace Foundry.Deploy.Services.DriverPacks;

public sealed record MicrosoftUpdateCatalogUpdate
{
    public required string UpdateId { get; init; }
    public required string Title { get; init; }
    public string Products { get; init; } = string.Empty;
    public string Classification { get; init; } = string.Empty;
    public DateTimeOffset? LastUpdated { get; init; }
    public string Version { get; init; } = string.Empty;
    public string Size { get; init; } = string.Empty;
    public long SizeInBytes { get; init; }
}
