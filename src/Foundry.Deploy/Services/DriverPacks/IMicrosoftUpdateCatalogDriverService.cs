namespace Foundry.Deploy.Services.DriverPacks;

public interface IMicrosoftUpdateCatalogDriverService
{
    Task<MicrosoftUpdateCatalogDriverResult> DownloadAsync(
        string destinationDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null);

    Task<MicrosoftUpdateCatalogDriverResult> ExpandAsync(
        string sourceDirectory,
        string destinationDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null);
}
