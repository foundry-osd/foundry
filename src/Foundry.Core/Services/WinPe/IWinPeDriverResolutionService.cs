namespace Foundry.Core.Services.WinPe;

public interface IWinPeDriverResolutionService
{
    Task<WinPeResult<IReadOnlyList<string>>> ResolveAsync(
        WinPeDriverResolutionRequest request,
        CancellationToken cancellationToken = default);
}
