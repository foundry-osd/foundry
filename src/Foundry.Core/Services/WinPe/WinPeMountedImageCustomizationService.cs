namespace Foundry.Core.Services.WinPe;

public sealed class WinPeMountedImageCustomizationService : IWinPeMountedImageCustomizationService
{
    private readonly IWinPeProcessRunner _processRunner;
    private readonly IWinPeDriverInjectionService _driverInjectionService;
    private readonly IWinPeImageInternationalizationService _imageInternationalizationService;
    private readonly IWinPeMountedImageAssetProvisioningService _assetProvisioningService;
    private readonly IWinPeRuntimePayloadProvisioningService _runtimePayloadProvisioningService;
    private readonly IWinReBootImagePreparationService _winReBootImagePreparationService;

    public WinPeMountedImageCustomizationService()
        : this(
            new WinPeProcessRunner(),
            new WinPeDriverInjectionService(),
            new WinPeImageInternationalizationService(),
            new WinPeMountedImageAssetProvisioningService(),
            new WinPeRuntimePayloadProvisioningService(),
            new WinReBootImagePreparationService())
    {
    }

    internal WinPeMountedImageCustomizationService(
        IWinPeProcessRunner processRunner,
        IWinPeDriverInjectionService driverInjectionService,
        IWinPeImageInternationalizationService imageInternationalizationService,
        IWinPeMountedImageAssetProvisioningService assetProvisioningService,
        IWinPeRuntimePayloadProvisioningService runtimePayloadProvisioningService,
        IWinReBootImagePreparationService winReBootImagePreparationService)
    {
        _processRunner = processRunner;
        _driverInjectionService = driverInjectionService;
        _imageInternationalizationService = imageInternationalizationService;
        _assetProvisioningService = assetProvisioningService;
        _runtimePayloadProvisioningService = runtimePayloadProvisioningService;
        _winReBootImagePreparationService = winReBootImagePreparationService;
    }

    public async Task<WinPeResult> CustomizeAsync(
        WinPeMountedImageCustomizationOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WinPeDiagnostic? validationError = ValidateOptions(options);
        if (validationError is not null)
        {
            return WinPeResult.Failure(validationError);
        }

        WinPeBuildArtifact artifact = options.Artifact!;
        WinPeToolPaths tools = options.Tools!;

        ReportProgress(options.Progress, 0, "Preparing boot image customization.");
        WinReBootImagePreparationResult? winRePreparationResult = null;
        if (options.BootImageSource == WinPeBootImageSource.WinReWifi)
        {
            WinPeResult<WinReBootImagePreparationResult> replaceResult =
                await _winReBootImagePreparationService.ReplaceBootWimAsync(
                    new WinReBootImagePreparationOptions
                    {
                        Artifact = artifact,
                        Tools = tools,
                        WinPeLanguage = options.WinPeLanguage,
                        CacheDirectoryPath = options.WinReCacheDirectoryPath,
                        CatalogUri = options.WinReCatalogUri ?? WinReBootImagePreparationService.DefaultOperatingSystemCatalogUri
                    },
                    cancellationToken).ConfigureAwait(false);

            if (!replaceResult.IsSuccess)
            {
                return WinPeResult.Failure(replaceResult.Error!);
            }

            winRePreparationResult = replaceResult.Value!;
        }

        ReportProgress(options.Progress, 30, "Mounting boot image.");
        WinPeResult<WinPeMountSession> mountResult = await WinPeMountSession.MountAsync(
            _processRunner,
            tools.DismPath,
            artifact.BootWimPath,
            artifact.MountDirectoryPath,
            artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);

        if (!mountResult.IsSuccess)
        {
            return WinPeResult.Failure(mountResult.Error!);
        }

        await using WinPeMountSession session = mountResult.Value!;

        if (options.BootImageSource == WinPeBootImageSource.WinReWifi)
        {
            if (winRePreparationResult is null)
            {
                return await FailWithDiscardAsync(
                    new WinPeDiagnostic(
                        WinPeErrorCodes.InternalError,
                        "WinRE Wi-Fi preparation did not return dependency metadata.",
                        null),
                    session,
                    cancellationToken).ConfigureAwait(false);
            }

            WinPeResult adjustmentsResult = ApplyWinReWifiAdjustments(session.MountDirectoryPath, winRePreparationResult);
            if (!adjustmentsResult.IsSuccess)
            {
                return await FailWithDiscardAsync(adjustmentsResult.Error!, session, cancellationToken).ConfigureAwait(false);
            }
        }

        ReportProgress(options.Progress, 45, "Injecting drivers into mounted image.");
        WinPeResult driverInjectionResult = await InjectDriversAsync(
            session.MountDirectoryPath,
            options.DriverPackagePaths,
            tools.DismPath,
            artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);

        if (!driverInjectionResult.IsSuccess)
        {
            return await FailWithDiscardAsync(driverInjectionResult.Error!, session, cancellationToken).ConfigureAwait(false);
        }

        ReportProgress(options.Progress, 65, "Applying language and optional components.");
        WinPeResult internationalizationResult = await _imageInternationalizationService.ApplyAsync(
            new WinPeImageInternationalizationOptions
            {
                MountedImagePath = session.MountDirectoryPath,
                Architecture = artifact.Architecture,
                Tools = tools,
                WinPeLanguage = options.WinPeLanguage,
                WorkingDirectoryPath = artifact.WorkingDirectoryPath
            },
            cancellationToken).ConfigureAwait(false);

        if (!internationalizationResult.IsSuccess)
        {
            return await FailWithDiscardAsync(internationalizationResult.Error!, session, cancellationToken).ConfigureAwait(false);
        }

        if (options.AssetProvisioning is not null)
        {
            ReportProgress(options.Progress, 80, "Provisioning Foundry boot assets.");
            WinPeResult assetProvisioningResult = await _assetProvisioningService.ProvisionAsync(
                options.AssetProvisioning with
                {
                    MountedImagePath = session.MountDirectoryPath,
                    Architecture = artifact.Architecture
                },
                cancellationToken).ConfigureAwait(false);

            if (!assetProvisioningResult.IsSuccess)
            {
                return await FailWithDiscardAsync(assetProvisioningResult.Error!, session, cancellationToken).ConfigureAwait(false);
            }
        }

        if (options.RuntimePayloadProvisioning is not null)
        {
            ReportProgress(options.Progress, 85, "Provisioning Foundry runtime payloads.");
            WinPeResult runtimePayloadResult = await _runtimePayloadProvisioningService.ProvisionAsync(
                options.RuntimePayloadProvisioning with
                {
                    MountedImagePath = session.MountDirectoryPath,
                    Architecture = artifact.Architecture
                },
                cancellationToken).ConfigureAwait(false);

            if (!runtimePayloadResult.IsSuccess)
            {
                return await FailWithDiscardAsync(runtimePayloadResult.Error!, session, cancellationToken).ConfigureAwait(false);
            }
        }

        ReportProgress(options.Progress, 90, "Committing image changes.");
        WinPeResult commitResult = await session.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (commitResult.IsSuccess)
        {
            ReportProgress(options.Progress, 100, "Image customization completed.");
        }

        return commitResult;
    }

    private async Task<WinPeResult> InjectDriversAsync(
        string mountedImagePath,
        IReadOnlyList<string> driverPackagePaths,
        string dismPath,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        if (driverPackagePaths.Count == 0)
        {
            return WinPeResult.Success();
        }

        return await _driverInjectionService.InjectAsync(
            new WinPeDriverInjectionOptions
            {
                MountedImagePath = mountedImagePath,
                DriverPackagePaths = driverPackagePaths,
                RecurseSubdirectories = true,
                DismExecutablePath = dismPath,
                WorkingDirectoryPath = workingDirectoryPath
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static WinPeResult ApplyWinReWifiAdjustments(
        string mountedImagePath,
        WinReBootImagePreparationResult preparationResult)
    {
        string system32Path = Path.Combine(mountedImagePath, "Windows", "System32");
        string winPeShellPath = Path.Combine(system32Path, "winpeshl.ini");

        try
        {
            Directory.CreateDirectory(system32Path);
            if (File.Exists(winPeShellPath))
            {
                File.Delete(winPeShellPath);
            }

            foreach (WinReDependencyFile dependencyFile in preparationResult.DependencyFiles)
            {
                if (!File.Exists(dependencyFile.StagedPath))
                {
                    return WinPeResult.Failure(
                        WinPeErrorCodes.WinReExtractionFailed,
                        $"The staged WinRE Wi-Fi dependency '{dependencyFile.FileName}' is missing.",
                        $"Expected path: '{dependencyFile.StagedPath}'.");
                }

                string destinationPath = Path.Combine(system32Path, dependencyFile.FileName);
                File.Copy(dependencyFile.StagedPath, destinationPath, overwrite: true);
            }

            return WinPeResult.Success();
        }
        catch (Exception ex)
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to apply WinRE Wi-Fi startup fixes to the mounted boot image.",
                ex.Message);
        }
    }

    private static async Task<WinPeResult> FailWithDiscardAsync(
        WinPeDiagnostic primaryDiagnostic,
        WinPeMountSession session,
        CancellationToken cancellationToken)
    {
        WinPeResult discardResult = await session.DiscardAsync(cancellationToken).ConfigureAwait(false);
        if (discardResult.IsSuccess)
        {
            return WinPeResult.Failure(primaryDiagnostic);
        }

        string details = string.Join(
            Environment.NewLine,
            primaryDiagnostic.Details ?? string.Empty,
            "Discard diagnostics:",
            discardResult.Error?.Details ?? string.Empty).Trim();

        return WinPeResult.Failure(new WinPeDiagnostic(
            primaryDiagnostic.Code,
            primaryDiagnostic.Message,
            details));
    }

    private static WinPeDiagnostic? ValidateOptions(WinPeMountedImageCustomizationOptions? options)
    {
        if (options is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Mounted image customization options are required.",
                "Provide a non-null WinPeMountedImageCustomizationOptions instance.");
        }

        if (options.Artifact is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "WinPE build artifact is required.",
                "Set WinPeMountedImageCustomizationOptions.Artifact.");
        }

        if (options.Tools is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "WinPE tool paths are required.",
                "Set WinPeMountedImageCustomizationOptions.Tools.");
        }

        if (!Enum.IsDefined(options.BootImageSource))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Boot image source value is invalid.",
                $"Value: '{options.BootImageSource}'.");
        }

        if (string.IsNullOrWhiteSpace(options.WinPeLanguage))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "WinPE language is required.",
                "Set WinPeMountedImageCustomizationOptions.WinPeLanguage.");
        }

        if (string.IsNullOrWhiteSpace(options.Tools.DismPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "DISM path is required.",
                "Set WinPeToolPaths.DismPath.");
        }

        if (!File.Exists(options.Artifact.BootWimPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "WinPE boot.wim was not found.",
                $"Expected path: '{options.Artifact.BootWimPath}'.");
        }

        if (options.BootImageSource == WinPeBootImageSource.WinReWifi &&
            string.IsNullOrWhiteSpace(options.WinReCacheDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "WinRE cache directory path is required for WinRE Wi-Fi boot image preparation.",
                "Set WinPeMountedImageCustomizationOptions.WinReCacheDirectoryPath.");
        }

        return null;
    }

    private static void ReportProgress(
        IProgress<WinPeMountedImageCustomizationProgress>? progress,
        int percent,
        string status)
    {
        progress?.Report(new WinPeMountedImageCustomizationProgress
        {
            Percent = percent,
            Status = status
        });
    }
}
