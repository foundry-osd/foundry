namespace Foundry.Core.Services.WinPe;

public interface IWinPeIsoMediaService
{
    Task<WinPeResult> CreateAsync(
        WinPeIsoMediaOptions options,
        CancellationToken cancellationToken = default);
}
