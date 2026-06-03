namespace Foundry.Deploy.Services.DriverPacks;

public sealed record MicrosoftUpdateCatalogDownload
{
    public string DownloadUrl { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string Sha1 { get; init; } = string.Empty;

    public string Sha256 { get; init; } = string.Empty;

    public string Architectures { get; init; } = string.Empty;

    public string Languages { get; init; } = string.Empty;
}
