namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Retrieves and parses WinPE driver catalog entries.
/// </summary>
public interface IWinPeDriverCatalogService
{
    /// <summary>
    /// Gets catalog entries matching the supplied options.
    /// </summary>
    /// <param name="options">The catalog lookup options.</param>
    /// <param name="cancellationToken">A token used to cancel network or file I/O.</param>
    /// <returns>The matching catalog entries or a diagnostic failure.</returns>
    Task<WinPeResult<IReadOnlyList<WinPeDriverCatalogEntry>>> GetCatalogAsync(
        WinPeDriverCatalogOptions options,
        CancellationToken cancellationToken = default);
}
