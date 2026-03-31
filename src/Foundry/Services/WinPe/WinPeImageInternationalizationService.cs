using Microsoft.Extensions.Logging;

namespace Foundry.Services.WinPe;

internal sealed class WinPeImageInternationalizationService : IWinPeImageInternationalizationService
{
    private readonly WinPeProcessRunner _processRunner;
    private readonly ILogger<WinPeImageInternationalizationService> _logger;

    public WinPeImageInternationalizationService(
        WinPeProcessRunner processRunner,
        ILogger<WinPeImageInternationalizationService> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<WinPeResult> ApplyAsync(
        string mountedImagePath,
        WinPeArchitecture architecture,
        WinPeToolPaths tools,
        string winPeLanguage,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        string normalizedLocale = WinPeLanguageUtility.Normalize(winPeLanguage);
        if (!WinPeLanguageUtility.TryResolveInputLocale(normalizedLocale, out string canonicalLocale, out string inputLocale))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.ValidationFailed,
                "Unable to resolve keyboard layout from selected WinPE language.",
                $"Selected language: '{normalizedLocale}'.");
        }

        _logger.LogInformation(
            "Adding required WinPE optional components. MountDirectoryPath={MountDirectoryPath}, WinPeLanguage={WinPeLanguage}",
            mountedImagePath,
            normalizedLocale);
        WinPeResult addComponentsResult = await AddRequiredOptionalComponentsAsync(
            mountedImagePath,
            architecture,
            tools,
            normalizedLocale,
            workingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!addComponentsResult.IsSuccess)
        {
            return addComponentsResult;
        }

        _logger.LogInformation("Required WinPE optional components added successfully. MountDirectoryPath={MountDirectoryPath}", mountedImagePath);
        _logger.LogInformation("Applying WinPE international settings. CanonicalLocale={CanonicalLocale}, InputLocale={InputLocale}", canonicalLocale, inputLocale);
        return await ApplyInternationalSettingsAsync(
            mountedImagePath,
            tools,
            canonicalLocale,
            inputLocale,
            workingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<WinPeResult> ApplyInternationalSettingsAsync(
        string mountedImagePath,
        WinPeToolPaths tools,
        string canonicalLocale,
        string inputLocale,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        string[] dismCommands =
        [
            $"/Image:{WinPeProcessRunner.Quote(mountedImagePath)} /Set-AllIntl:{canonicalLocale}",
            $"/Image:{WinPeProcessRunner.Quote(mountedImagePath)} /Set-InputLocale:{inputLocale}"
        ];

        foreach (string args in dismCommands)
        {
            WinPeProcessExecution execution = await _processRunner.RunAsync(
                tools.DismPath,
                args,
                workingDirectoryPath,
                cancellationToken).ConfigureAwait(false);

            if (!execution.IsSuccess)
            {
                return WinPeResult.Failure(
                    WinPeErrorCodes.BuildFailed,
                    "Failed to apply WinPE international settings.",
                    execution.ToDiagnosticText());
            }
        }

        return WinPeResult.Success();
    }

    private async Task<WinPeResult> AddRequiredOptionalComponentsAsync(
        string mountedImagePath,
        WinPeArchitecture architecture,
        WinPeToolPaths tools,
        string winPeLanguage,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        string ocRoot = GetOptionalComponentsRootPath(tools.KitsRootPath, architecture);

        if (!Directory.Exists(ocRoot))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.ToolNotFound,
                "WinPE optional components folder was not found.",
                $"Expected path: '{ocRoot}'.");
        }

        string[] components =
        [
            "WinPE-WMI",
            "WinPE-NetFX",
            "WinPE-Scripting",
            "WinPE-PowerShell",
            "WinPE-WinReCfg",
            "WinPE-DismCmdlets",
            "WinPE-StorageWMI",
            "WinPE-Dot3Svc",
            "WinPE-EnhancedStorage"
        ];
        string normalizedLocale = WinPeLanguageUtility.Normalize(winPeLanguage);
        string languagePackCab = Path.Combine(ocRoot, normalizedLocale, "lp.cab");
        if (File.Exists(languagePackCab))
        {
            WinPeProcessExecution addLanguagePack = await _processRunner.RunAsync(
                tools.DismPath,
                $"/Image:{WinPeProcessRunner.Quote(mountedImagePath)} /Add-Package /PackagePath:{WinPeProcessRunner.Quote(languagePackCab)}",
                workingDirectoryPath,
                cancellationToken).ConfigureAwait(false);

            if (!addLanguagePack.IsSuccess && !IsIgnorablePackageFailure(addLanguagePack))
            {
                return WinPeResult.Failure(
                    WinPeErrorCodes.BuildFailed,
                    "Failed to add WinPE language pack.",
                    addLanguagePack.ToDiagnosticText());
            }
        }
        else
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.ToolNotFound,
                "The selected WinPE language pack was not found.",
                $"Expected package: '{languagePackCab}'.");
        }

        int installed = 0;
        foreach (string component in components)
        {
            string neutral = Path.Combine(ocRoot, $"{component}.cab");
            if (File.Exists(neutral))
            {
                WinPeProcessExecution addNeutral = await _processRunner.RunAsync(
                    tools.DismPath,
                    $"/Image:{WinPeProcessRunner.Quote(mountedImagePath)} /Add-Package /PackagePath:{WinPeProcessRunner.Quote(neutral)}",
                    workingDirectoryPath,
                    cancellationToken).ConfigureAwait(false);

                if (!addNeutral.IsSuccess && !IsIgnorablePackageFailure(addNeutral))
                {
                    return WinPeResult.Failure(
                        WinPeErrorCodes.BuildFailed,
                        $"Failed to add optional component '{component}'.",
                        addNeutral.ToDiagnosticText());
                }

                installed++;
            }

            string localeCab = Path.Combine(ocRoot, normalizedLocale, $"{component}_{normalizedLocale}.cab");

            if (File.Exists(localeCab))
            {
                WinPeProcessExecution addLocale = await _processRunner.RunAsync(
                    tools.DismPath,
                    $"/Image:{WinPeProcessRunner.Quote(mountedImagePath)} /Add-Package /PackagePath:{WinPeProcessRunner.Quote(localeCab)}",
                    workingDirectoryPath,
                    cancellationToken).ConfigureAwait(false);

                if (!addLocale.IsSuccess && !IsIgnorablePackageFailure(addLocale))
                {
                    return WinPeResult.Failure(
                        WinPeErrorCodes.BuildFailed,
                        $"Failed to add localized optional component '{component}'.",
                        addLocale.ToDiagnosticText());
                }
            }
        }

        return installed > 0
            ? WinPeResult.Success()
            : WinPeResult.Failure(
                WinPeErrorCodes.ToolNotFound,
                "Required WinPE optional components were not found in ADK.",
                $"No required component CAB was found under '{ocRoot}'.");
    }

    private static string GetOptionalComponentsRootPath(string kitsRootPath, WinPeArchitecture architecture)
    {
        return Path.Combine(
            kitsRootPath,
            "Assessment and Deployment Kit",
            "Windows Preinstallation Environment",
            architecture.ToCopypeArchitecture(),
            "WinPE_OCs");
    }

    private static bool IsIgnorablePackageFailure(WinPeProcessExecution execution)
    {
        string diagnostic = $"{execution.StandardOutput}{Environment.NewLine}{execution.StandardError}";
        return diagnostic.Contains("0x800f081e", StringComparison.OrdinalIgnoreCase) ||
               diagnostic.Contains("not applicable", StringComparison.OrdinalIgnoreCase) ||
               diagnostic.Contains("already installed", StringComparison.OrdinalIgnoreCase) ||
               diagnostic.Contains("already exists", StringComparison.OrdinalIgnoreCase);
    }
}
