namespace Foundry.Core.Services.WinPe;

public interface IWinPeImageInternationalizationService
{
    Task<WinPeResult> ApplyAsync(
        WinPeImageInternationalizationOptions options,
        CancellationToken cancellationToken = default);
}
