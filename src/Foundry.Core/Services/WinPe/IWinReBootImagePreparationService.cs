namespace Foundry.Core.Services.WinPe;

public interface IWinReBootImagePreparationService
{
    Task<WinPeResult<WinReBootImagePreparationResult>> ReplaceBootWimAsync(
        WinReBootImagePreparationOptions options,
        CancellationToken cancellationToken = default);
}
