namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Downloads, verifies, and extracts driver packages selected from the catalog.
/// </summary>
public interface IWinPeDriverPackageService
{
    /// <summary>
    /// Prepares selected driver packages for injection.
    /// </summary>
    /// <param name="packages">The catalog packages to prepare.</param>
    /// <param name="downloadRootPath">The directory used for downloaded package files.</param>
    /// <param name="extractRootPath">The directory used for extracted package content.</param>
    /// <param name="downloadProgress">An optional progress reporter for downloads.</param>
    /// <param name="cancellationToken">A token used to cancel package preparation.</param>
    /// <returns>The prepared driver set or a diagnostic failure.</returns>
    Task<WinPeResult<WinPePreparedDriverSet>> PrepareAsync(
        IReadOnlyList<WinPeDriverCatalogEntry> packages,
        string downloadRootPath,
        string extractRootPath,
        IProgress<WinPeDownloadProgress>? downloadProgress,
        CancellationToken cancellationToken);
}
