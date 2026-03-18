namespace Foundry.Services.WinPe;

internal interface IWinPeImageInternationalizationService
{
    bool TryResolveInputLocale(string languageCode, out string canonicalLanguageCode, out string inputLocale);

    string NormalizeWinPeLanguageCode(string languageCode);

    Task<WinPeResult> ApplyAsync(
        string mountedImagePath,
        WinPeArchitecture architecture,
        WinPeToolPaths tools,
        string winPeLanguage,
        string workingDirectoryPath,
        CancellationToken cancellationToken);
}
