namespace Foundry.Services.WinPe;

internal interface IWinPeImageInternationalizationService
{
    Task<WinPeResult> ApplyAsync(
        string mountedImagePath,
        WinPeArchitecture architecture,
        WinPeToolPaths tools,
        string winPeLanguage,
        string workingDirectoryPath,
        CancellationToken cancellationToken);
}
