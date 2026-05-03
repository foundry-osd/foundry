namespace Foundry.Core.Services.WinPe;

public interface IWinPeDriverPackageService
{
    Task<WinPeResult<WinPePreparedDriverSet>> PrepareAsync(
        IReadOnlyList<WinPeDriverCatalogEntry> packages,
        string downloadRootPath,
        string extractRootPath,
        CancellationToken cancellationToken);
}
