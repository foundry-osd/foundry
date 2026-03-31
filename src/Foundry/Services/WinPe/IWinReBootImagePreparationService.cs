namespace Foundry.Services.WinPe;

internal interface IWinReBootImagePreparationService
{
    Task<WinPeResult> ReplaceBootWimAsync(
        WinPeBuildArtifact artifact,
        WinPeToolPaths tools,
        string winPeLanguage,
        CancellationToken cancellationToken);
}
