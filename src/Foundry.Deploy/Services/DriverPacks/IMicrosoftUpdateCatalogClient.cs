namespace Foundry.Deploy.Services.DriverPacks;

public interface IMicrosoftUpdateCatalogClient
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MicrosoftUpdateCatalogUpdate>> SearchAsync(
        string searchQuery,
        bool descending = true,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetDownloadUrlsAsync(
        string updateId,
        CancellationToken cancellationToken = default);
}
