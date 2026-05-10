namespace Foundry.Core.Services.WinPe;

public interface IWinPeWorkspacePreparationService
{
    Task<WinPeResult<WinPeWorkspacePreparationResult>> PrepareAsync(
        WinPeWorkspacePreparationOptions options,
        CancellationToken cancellationToken = default);
}
