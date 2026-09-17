// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed class WinPeMountedImageCustomizationService : IWinPeMountedImageCustomizationService
{
    private readonly IWinPeProcessRunner _processRunner;
    private readonly IWinPeDriverInjectionService _driverInjectionService;
    private readonly IWinPeImageInternationalizationService _imageInternationalizationService;
    private readonly IWinPeMountedImageAssetProvisioningService _assetProvisioningService;
    private readonly IWinPeRuntimePayloadProvisioningService _runtimePayloadProvisioningService;
    private readonly IWinPeBootImagePreparationService _bootImagePreparationService;

    public WinPeMountedImageCustomizationService()
        : this(
            new WinPeProcessRunner(),
            new WinPeDriverInjectionService(),
            new WinPeImageInternationalizationService(),
            new WinPeMountedImageAssetProvisioningService(),
            new WinPeRuntimePayloadProvisioningService(),
            new WinPeBootImagePreparationService())
    {
    }

    internal WinPeMountedImageCustomizationService(
        IWinPeProcessRunner processRunner,
        IWinPeDriverInjectionService driverInjectionService,
        IWinPeImageInternationalizationService imageInternationalizationService,
        IWinPeMountedImageAssetProvisioningService assetProvisioningService,
        IWinPeRuntimePayloadProvisioningService runtimePayloadProvisioningService,
        IWinPeBootImagePreparationService bootImagePreparationService)
    {
        _processRunner = processRunner;
        _driverInjectionService = driverInjectionService;
        _imageInternationalizationService = imageInternationalizationService;
        _assetProvisioningService = assetProvisioningService;
        _runtimePayloadProvisioningService = runtimePayloadProvisioningService;
        _bootImagePreparationService = bootImagePreparationService;
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
        WinPeBootImagePreparationResult? preparationResult = null;
        if (RequiresSourcePreparation(options))
        {
            WinPeResult<WinPeBootImagePreparationResult> sourceResult =
                await _bootImagePreparationService.PrepareAsync(
                    new WinPeBootImagePreparationOptions
                    {
                        Artifact = artifact,
                        Tools = tools,
                        BootImageSource = options.BootImageSource,
                        WinPeLanguage = options.WinPeLanguage,
                        CacheDirectoryPath = options.WinReCacheDirectoryPath,
                        CatalogUri = options.WinReCatalogUri ?? WinPeBootImagePreparationService.DefaultOperatingSystemCatalogUri,
                        DownloadProgress = options.DownloadProgress,
                        Progress = options.Progress
                    },
                    cancellationToken).ConfigureAwait(false);

            if (!sourceResult.IsSuccess)
            {
                return WinPeResult.Failure(sourceResult.Error!);
            }

            preparationResult = sourceResult.Value!;
        }

        ReportProgress(options.Progress, 30, "Mounting boot image.");
        cancellationToken.ThrowIfCancellationRequested();
        // DISM mutations finish before cancellation is observed; ownership of a successful
        // mount must be captured first so cancellation always disposes the mount session.
        WinPeResult<WinPeMountSession> mountResult = await WinPeMountSession.MountAsync(
            _processRunner,
            tools.DismPath,
            artifact.BootWimPath,
            artifact.MountDirectoryPath,
            artifact.WorkingDirectoryPath,
            CancellationToken.None,
            CreateDismProgress(options.Progress, 30, "Mounting boot image.")).ConfigureAwait(false);

        if (!mountResult.IsSuccess)
        {
            return WinPeResult.Failure(mountResult.Error!);
        }

        await using WinPeMountSession session = mountResult.Value!;
        cancellationToken.ThrowIfCancellationRequested();

        if (options.BootImageSource == WinPeBootImageSource.WinReWifi)
        {
            if (preparationResult is null)
            {
                return await FailWithDiscardAsync(
                    new WinPeDiagnostic(
                        WinPeErrorCodes.InternalError,
                        "WinRE Wi-Fi preparation did not return dependency metadata.",
                        null),
                    session).ConfigureAwait(false);
            }

            WinPeResult adjustmentsResult = ApplyWinReWifiAdjustments(session.MountDirectoryPath, preparationResult);
            if (!adjustmentsResult.IsSuccess)
            {
                return await FailWithDiscardAsync(adjustmentsResult.Error!, session).ConfigureAwait(false);
            }
        }

        ReportProgress(options.Progress, 45, "Injecting drivers into mounted image.");
        cancellationToken.ThrowIfCancellationRequested();
        WinPeResult driverInjectionResult = await InjectDriversAsync(
            session.MountDirectoryPath,
            options.DriverPackagePaths,
            tools.DismPath,
            artifact.WorkingDirectoryPath,
            options.Progress,
            CancellationToken.None).ConfigureAwait(false);

        if (!driverInjectionResult.IsSuccess)
        {
            return await FailWithDiscardAsync(driverInjectionResult.Error!, session).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();

        ReportProgress(options.Progress, 65, "Applying language and optional components.");
        WinPeResult internationalizationResult = await _imageInternationalizationService.ApplyAsync(
            new WinPeImageInternationalizationOptions
            {
                MountedImagePath = session.MountDirectoryPath,
                Architecture = artifact.Architecture,
                Tools = tools,
                WinPeLanguage = options.WinPeLanguage,
                WorkingDirectoryPath = artifact.WorkingDirectoryPath,
                DismProgress = CreateDismProgress(options.Progress, 65, "Applying language and optional components.")
            },
            CancellationToken.None).ConfigureAwait(false);

        if (!internationalizationResult.IsSuccess)
        {
            return await FailWithDiscardAsync(internationalizationResult.Error!, session).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();

        if (preparationResult is not null)
        {
            // Optional components can provide graphics files; preserve those before adding missing dependencies.
            WinPeResult dependenciesResult = ApplyDependencyFiles(
                session.MountDirectoryPath,
                preparationResult.DependencyFiles.Where(file => !file.OverwriteExisting));
            if (!dependenciesResult.IsSuccess)
            {
                return await FailWithDiscardAsync(dependenciesResult.Error!, session).ConfigureAwait(false);
            }
        }

        if (options.RuntimePayloadProvisioning is not null)
        {
            ReportProgress(options.Progress, 80, "Provisioning Foundry runtime payloads.");
            WinPeResult runtimePayloadResult = await _runtimePayloadProvisioningService.ProvisionAsync(
                options.RuntimePayloadProvisioning with
                {
                    MountedImagePath = session.MountDirectoryPath,
                    Architecture = artifact.Architecture
                },
                options.DownloadProgress,
                cancellationToken).ConfigureAwait(false);

            if (!runtimePayloadResult.IsSuccess)
            {
                return await FailWithDiscardAsync(runtimePayloadResult.Error!, session).ConfigureAwait(false);
            }
        }

        if (options.AssetProvisioning is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReportProgress(options.Progress, 85, "Provisioning Foundry boot assets.");
            WinPeResult assetProvisioningResult = await _assetProvisioningService.ProvisionAsync(
                options.AssetProvisioning with
                {
                    MountedImagePath = session.MountDirectoryPath,
                    Architecture = artifact.Architecture
                },
                CancellationToken.None).ConfigureAwait(false);

            if (!assetProvisioningResult.IsSuccess)
            {
                return await FailWithDiscardAsync(assetProvisioningResult.Error!, session).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        ReportProgress(options.Progress, 90, "Committing image changes.");
        cancellationToken.ThrowIfCancellationRequested();
        WinPeResult commitResult = await session.CommitAsync(
            CancellationToken.None,
            CreateDismProgress(options.Progress, 90, "Committing image changes.")).ConfigureAwait(false);
        if (commitResult.IsSuccess)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReportProgress(options.Progress, 100, "Image customization completed.");
        }

        return commitResult;
    }

    private async Task<WinPeResult> InjectDriversAsync(
        string mountedImagePath,
        IReadOnlyList<string> driverPackagePaths,
        string dismPath,
        string workingDirectoryPath,
        IProgress<WinPeMountedImageCustomizationProgress>? progress,
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
                WorkingDirectoryPath = workingDirectoryPath,
                DismProgress = CreateDismProgress(progress, 45, "Injecting drivers into mounted image.")
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static WinPeResult ApplyWinReWifiAdjustments(
        string mountedImagePath,
        WinPeBootImagePreparationResult preparationResult)
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

            return ApplyDependencyFiles(mountedImagePath, preparationResult.DependencyFiles.Where(file => file.OverwriteExisting));
        }
        catch (Exception ex)
        {
            return WinPeResult.Failure(new WinPeDiagnostic(
                WinPeErrorCodes.BuildFailed,
                "Failed to apply WinRE Wi-Fi startup fixes to the mounted boot image.",
                ex.Message,
                exception: ex));
        }
    }

    /// <summary>
    /// Copies staged Windows dependencies, retaining image-provided files when replacement is not required.
    /// </summary>
    private static WinPeResult ApplyDependencyFiles(string mountedImagePath, IEnumerable<WinPeDependencyFile> dependencyFiles)
    {
        string system32Path = Path.Combine(mountedImagePath, "Windows", "System32");
        try
        {
            Directory.CreateDirectory(system32Path);
            foreach (WinPeDependencyFile dependencyFile in dependencyFiles)
            {
                string destinationPath = Path.Combine(system32Path, dependencyFile.FileName);
                if (!dependencyFile.OverwriteExisting && File.Exists(destinationPath))
                {
                    continue;
                }

                if (!File.Exists(dependencyFile.StagedPath))
                {
                    return WinPeResult.Failure(
                        WinPeErrorCodes.WinReExtractionFailed,
                        $"The staged boot image dependency '{dependencyFile.FileName}' is missing.",
                        $"Expected path: '{dependencyFile.StagedPath}'.");
                }

                File.Copy(dependencyFile.StagedPath, destinationPath, overwrite: dependencyFile.OverwriteExisting);
            }

            return WinPeResult.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return WinPeResult.Failure(new WinPeDiagnostic(
                WinPeErrorCodes.BuildFailed,
                "Failed to provision Windows dependencies in the mounted boot image.",
                ex.Message,
                exception: ex));
        }
    }

    /// <summary>
    /// Windows source extraction is shared by Wi-Fi recovery media and ARM64 graphics preparation.
    /// </summary>
    private static bool RequiresSourcePreparation(WinPeMountedImageCustomizationOptions options) =>
        options.BootImageSource == WinPeBootImageSource.WinReWifi || options.Artifact!.Architecture == WinPeArchitecture.Arm64;

    private static async Task<WinPeResult> FailWithDiscardAsync(
        WinPeDiagnostic primaryDiagnostic,
        WinPeMountSession session)
    {
        WinPeResult discardResult = await session.DiscardAsync(CancellationToken.None).ConfigureAwait(false);
        if (discardResult.IsSuccess)
        {
            return WinPeResult.Failure(primaryDiagnostic);
        }

        string details = string.Join(
            Environment.NewLine,
            primaryDiagnostic.Details ?? string.Empty,
            "Discard diagnostics:",
            discardResult.Error?.Details ?? string.Empty).Trim();

        return WinPeResult.Failure(primaryDiagnostic with { Details = details });
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

        if (RequiresSourcePreparation(options) && string.IsNullOrWhiteSpace(options.WinReCacheDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "A Windows source cache directory is required for Wi-Fi or ARM64 boot image preparation.",
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

    private static IProgress<WinPeDismProgress>? CreateDismProgress(
        IProgress<WinPeMountedImageCustomizationProgress>? progress,
        int percent,
        string status)
    {
        return progress is null ? null : new WinPeDismProgressForwarder(progress, percent, status);
    }
}
