namespace Foundry.Deploy.Services.DriverPacks;

public sealed record MicrosoftUpdateCatalogDownloadedDriver
{
    public required string UpdateId { get; init; }
    public required string Title { get; init; }
    public string Version { get; init; } = string.Empty;
    public string Size { get; init; } = string.Empty;
    public required string DownloadUrl { get; init; }
}
