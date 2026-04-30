namespace Foundry.Services.WinPe;

public interface IWinPeDriverCatalogService
{
    Task<WinPeResult<IReadOnlyList<WinPeDriverCatalogEntry>>> GetCatalogAsync(
        WinPeDriverCatalogOptions options,
        CancellationToken cancellationToken = default);
}
