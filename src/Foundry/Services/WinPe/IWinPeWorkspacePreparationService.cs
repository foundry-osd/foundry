namespace Foundry.Services.WinPe;

internal interface IWinPeWorkspacePreparationService
{
    Task<WinPeResult<WinPeWorkspacePreparationResult>> PrepareAsync(
        WinPeWorkspacePreparationRequest request,
        IProgress<WinPeWorkspacePreparationStage>? progress,
        CancellationToken cancellationToken);
}
