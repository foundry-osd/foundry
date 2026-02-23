namespace Foundry.Deploy.Services.DriverPacks;

public interface IMicrosoftUpdateCatalogDriverService
{
    Task<MicrosoftUpdateCatalogDriverResult> DownloadAsync(
        string destinationDirectory,
        CancellationToken cancellationToken = default);
}
