namespace Foundry.Services.WinPe;

internal interface IWinPeDriverResolutionService
{
    Task<WinPeResult<IReadOnlyList<string>>> ResolveAsync(
        WinPeDriverResolutionRequest request,
        CancellationToken cancellationToken);
}
