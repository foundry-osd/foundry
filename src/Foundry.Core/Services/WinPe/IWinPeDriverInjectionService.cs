namespace Foundry.Core.Services.WinPe;

public interface IWinPeDriverInjectionService
{
    Task<WinPeResult> InjectAsync(
        WinPeDriverInjectionOptions options,
        CancellationToken cancellationToken = default);
}
