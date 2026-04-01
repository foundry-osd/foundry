namespace Foundry.Services.WinPe;

internal interface IWinReBootImagePreparationService
{
    Task<WinPeResult<WinReBootImagePreparationResult>> ReplaceBootWimAsync(
        WinPeBuildArtifact artifact,
        WinPeToolPaths tools,
        string winPeLanguage,
        IProgress<WinPeMountedImageCustomizationProgress>? progress,
        CancellationToken cancellationToken);
}
