// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed class WinPeImageInternationalizationService : IWinPeImageInternationalizationService
{
    private readonly IWinPeProcessRunner _processRunner;

    public WinPeImageInternationalizationService()
        : this(new WinPeProcessRunner())
    {
    }

    internal WinPeImageInternationalizationService(IWinPeProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<WinPeResult> ApplyAsync(
        WinPeImageInternationalizationOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WinPeDiagnostic? validationError = ValidateOptions(options);
        if (validationError is not null)
        {
            return WinPeResult.Failure(validationError);
        }

        WinPeToolPaths tools = options.Tools!;
        string normalizedLocale = WinPeLanguageUtility.Normalize(options.WinPeLanguage);
        if (!WinPeLanguageUtility.TryResolveInputLocale(normalizedLocale, out string canonicalLocale, out string inputLocale))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.ValidationFailed,
                "The selected WinPE language cannot be converted to a keyboard layout.",
                $"Language: '{options.WinPeLanguage}'.");
        }

        var capabilities = new WinPeCapabilityValidationService(_processRunner);
        WinPeResult<IReadOnlyList<WinPeCapabilityValidationService.InstalledPackage>> inventory =
            await capabilities.ReadInventoryAsync(options, cancellationToken).ConfigureAwait(false);
        if (!inventory.IsSuccess)
        {
            return WinPeResult.Failure(inventory.Error!);
        }

        string optionalComponentsRoot = GetOptionalComponentsRootPath(tools.KitsRootPath, options.Architecture);
        foreach (WinPeCapabilityValidationService.RequiredPackage package in WinPeCapabilityValidationService.GetRequiredPackages(normalizedLocale))
        {
            if (WinPeCapabilityValidationService.FindInstalled(inventory.Value!, package, options.Architecture) is not null)
            {
                continue;
            }
            string path = Path.Combine(optionalComponentsRoot, package.RelativeCabPath);
            if (!File.Exists(path))
            {
                return WinPeResult.Failure(WinPeErrorCodes.ToolNotFound,
                    $"The required WinPE package '{package.Key}' was not found.", $"Expected path: '{path}'.");
            }
            WinPeProcessExecution execution = await WinPeDismProcessRunner.RunAsync(_processRunner, tools.DismPath,
                ["/English", $"/Image:{options.MountedImagePath}", "/Add-Package", $"/PackagePath:{path}"],
                options.WorkingDirectoryPath, "Applying required WinPE packages with DISM.", options.DismProgress,
                cancellationToken).ConfigureAwait(false);
            if (!execution.IsSuccess)
            {
                inventory = await capabilities.ReadInventoryAsync(options, cancellationToken).ConfigureAwait(false);
                if (!inventory.IsSuccess)
                {
                    return WinPeResult.Failure(inventory.Error!);
                }
                if (WinPeCapabilityValidationService.FindInstalled(inventory.Value!, package, options.Architecture) is null)
                {
                    return WinPeResult.Failure(WinPeErrorCodes.BuildFailed,
                        $"Failed to install required WinPE package '{package.Key}'.", execution.ToDiagnosticText());
                }
            }
        }

        WinPeResult settings = await ApplyInternationalSettingsAsync(options.MountedImagePath, tools.DismPath,
            canonicalLocale, inputLocale, options.WorkingDirectoryPath, options.DismProgress, cancellationToken).ConfigureAwait(false);
        if (!settings.IsSuccess)
        {
            return settings;
        }
        WinPeResult<WinPeCapabilityValidationResult> validation = await capabilities.ValidateAsync(options, cancellationToken).ConfigureAwait(false);
        return validation.IsSuccess ? WinPeResult.Success() : WinPeResult.Failure(validation.Error!);
    }
    private async Task<WinPeResult> ApplyInternationalSettingsAsync(
        string mountedImagePath,
        string dismPath,
        string canonicalLocale,
        string inputLocale,
        string workingDirectoryPath,
        IProgress<WinPeDismProgress>? dismProgress,
        CancellationToken cancellationToken)
    {
        string[][] arguments =
        [
            [$"/Image:{mountedImagePath}", $"/Set-AllIntl:{canonicalLocale}"],
            [$"/Image:{mountedImagePath}", $"/Set-InputLocale:{inputLocale}"]
        ];

        foreach (string[] args in arguments)
        {
            WinPeProcessExecution execution = await WinPeDismProcessRunner.RunAsync(
                _processRunner,
                dismPath,
                args,
                workingDirectoryPath,
                "Applying international settings with DISM.",
                dismProgress,
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

    internal static WinPeDiagnostic? ValidateOptions(WinPeImageInternationalizationOptions? options)
    {
        if (options is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Internationalization options are required.",
                "Provide a non-null WinPeImageInternationalizationOptions instance.");
        }

        if (string.IsNullOrWhiteSpace(options.MountedImagePath) || !Directory.Exists(options.MountedImagePath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Mounted image path is required.",
                $"Path: '{options.MountedImagePath}'.");
        }

        if (!Enum.IsDefined(options.Architecture))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Architecture value is invalid.",
                $"Value: '{options.Architecture}'.");
        }

        if (options.Tools is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "WinPE tool paths are required.",
                "Set WinPeImageInternationalizationOptions.Tools.");
        }

        if (string.IsNullOrWhiteSpace(options.Tools.KitsRootPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "ADK kits root path is required.",
                "Set WinPeToolPaths.KitsRootPath.");
        }

        if (string.IsNullOrWhiteSpace(options.Tools.DismPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "DISM path is required.",
                "Set WinPeToolPaths.DismPath.");
        }

        if (string.IsNullOrWhiteSpace(options.WinPeLanguage))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "WinPE language is required.",
                "Set WinPeImageInternationalizationOptions.WinPeLanguage.");
        }

        if (string.IsNullOrWhiteSpace(options.WorkingDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Working directory path is required.",
                "Set WinPeImageInternationalizationOptions.WorkingDirectoryPath.");
        }

        return null;
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

}
