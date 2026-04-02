using Microsoft.Extensions.Logging;

namespace Foundry.Services.WinPe;

internal sealed class WinPeMountedImageCustomizationService : IWinPeMountedImageCustomizationService
{
    private readonly IWinPeDriverInjectionService _driverInjectionService;
    private readonly IWinPeImageInternationalizationService _imageInternationalizationService;
    private readonly IWinPeLocalConnectEmbeddingService _localConnectEmbeddingService;
    private readonly IWinPeLocalDeployEmbeddingService _localDeployEmbeddingService;
    private readonly IWinPeMountedImageAssetProvisioningService _mountedImageAssetProvisioningService;
    private readonly IWinReBootImagePreparationService _winReBootImagePreparationService;
    private readonly WinPeProcessRunner _processRunner;
    private readonly ILogger<WinPeMountedImageCustomizationService> _logger;

    public WinPeMountedImageCustomizationService(
        IWinPeDriverInjectionService driverInjectionService,
        IWinPeImageInternationalizationService imageInternationalizationService,
        IWinPeLocalConnectEmbeddingService localConnectEmbeddingService,
        IWinPeLocalDeployEmbeddingService localDeployEmbeddingService,
        IWinPeMountedImageAssetProvisioningService mountedImageAssetProvisioningService,
        IWinReBootImagePreparationService winReBootImagePreparationService,
        WinPeProcessRunner processRunner,
        ILogger<WinPeMountedImageCustomizationService> logger)
    {
        _driverInjectionService = driverInjectionService;
        _imageInternationalizationService = imageInternationalizationService;
        _localConnectEmbeddingService = localConnectEmbeddingService;
        _localDeployEmbeddingService = localDeployEmbeddingService;
        _mountedImageAssetProvisioningService = mountedImageAssetProvisioningService;
        _winReBootImagePreparationService = winReBootImagePreparationService;
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<WinPeResult> CustomizeAsync(
        WinPeMountedImageCustomizationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ReportProgress(request.Progress, 0, "Preparing boot image customization.");
        WinReBootImagePreparationResult? winRePreparationResult = null;

        if (request.BootImageSource == WinPeBootImageSource.WinReWifi)
        {
            _logger.LogInformation(
                "Replacing WinPE boot.wim with WinRE source before customization. WorkingDirectoryPath={WorkingDirectoryPath}, WinPeLanguage={WinPeLanguage}",
                request.Artifact.WorkingDirectoryPath,
                request.WinPeLanguage);
            WinPeResult<WinReBootImagePreparationResult> replaceBootImage = await _winReBootImagePreparationService.ReplaceBootWimAsync(
                request.Artifact,
                request.Tools,
                request.WinPeLanguage,
                CreateNestedProgress(request.Progress, 0, 25),
                cancellationToken).ConfigureAwait(false);
            if (!replaceBootImage.IsSuccess)
            {
                return WinPeResult.Failure(replaceBootImage.Error!);
            }

            winRePreparationResult = replaceBootImage.Value!;
        }
        else
        {
            ReportProgress(request.Progress, 25, "Using standard WinPE boot image.");
        }

        ReportProgress(request.Progress, 30, "Mounting boot image.");
        _logger.LogInformation(
            "Mounting WinPE image for customization. BootWimPath={BootWimPath}, MountDirectoryPath={MountDirectoryPath}, WinPeLanguage={WinPeLanguage}",
            request.Artifact.BootWimPath,
            request.Artifact.MountDirectoryPath,
            request.WinPeLanguage);
        WinPeResult<WinPeMountSession> mount = await WinPeMountSession.MountAsync(
            _processRunner,
            request.Tools.DismPath,
            request.Artifact.BootWimPath,
            request.Artifact.MountDirectoryPath,
            request.Artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!mount.IsSuccess)
        {
            return WinPeResult.Failure(mount.Error!);
        }

        await using WinPeMountSession session = mount.Value!;
        _logger.LogInformation("Mounted WinPE image for customization. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);

        if (request.BootImageSource == WinPeBootImageSource.WinReWifi)
        {
            if (winRePreparationResult is null)
            {
                return await FailWithDiscardAsync(
                    new WinPeDiagnostic(
                        WinPeErrorCodes.InternalError,
                        "The WinRE Wi-Fi boot image was prepared without dependency metadata."),
                    session,
                    cancellationToken).ConfigureAwait(false);
            }

            ReportProgress(request.Progress, 35, "Applying WinRE Wi-Fi startup fixes.");
            WinPeResult winReAdjustmentResult = ApplyWinReWifiAdjustments(
                session.MountDirectoryPath,
                winRePreparationResult);
            if (!winReAdjustmentResult.IsSuccess)
            {
                return await FailWithDiscardAsync(winReAdjustmentResult.Error!, session, cancellationToken).ConfigureAwait(false);
            }
        }

        ReportProgress(request.Progress, 40, "Injecting drivers into mounted image.");
        WinPeResult inject = await InjectDriversAsync(
            session.MountDirectoryPath,
            request.DriverDirectories,
            request.Tools.DismPath,
            request.Artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!inject.IsSuccess)
        {
            return await FailWithDiscardAsync(inject.Error!, session, cancellationToken).ConfigureAwait(false);
        }

        ReportProgress(request.Progress, 55, "Applying language and optional components.");
        WinPeResult internationalizationResult = await _imageInternationalizationService.ApplyAsync(
            session.MountDirectoryPath,
            request.Artifact.Architecture,
            request.Tools,
            request.WinPeLanguage,
            request.Artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!internationalizationResult.IsSuccess)
        {
            return await FailWithDiscardAsync(internationalizationResult.Error!, session, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Applied WinPE international settings successfully. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);

        ReportProgress(request.Progress, 66, "Provisioning Foundry.Connect payload.");
        WinPeResult localConnectProvisioning = await _localConnectEmbeddingService.ProvisionAsync(
            session.MountDirectoryPath,
            request.Artifact.Architecture,
            request.Artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!localConnectProvisioning.IsSuccess)
        {
            return await FailWithDiscardAsync(localConnectProvisioning.Error!, session, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Provisioned Foundry.Connect payload into mounted WinPE image. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);

        ReportProgress(request.Progress, 72, "Provisioning Foundry.Deploy payload.");
        WinPeResult localDeployProvisioning = await _localDeployEmbeddingService.ProvisionAsync(
            session.MountDirectoryPath,
            request.Artifact.Architecture,
            request.Artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!localDeployProvisioning.IsSuccess)
        {
            return await FailWithDiscardAsync(localDeployProvisioning.Error!, session, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Provisioned Foundry.Deploy payload into mounted WinPE image. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        _logger.LogInformation(
            "Starting mounted image asset provisioning. MountDirectoryPath={MountDirectoryPath}, AutopilotProfileCount={AutopilotProfileCount}, HasExpertConfiguration={HasExpertConfiguration}",
            session.MountDirectoryPath,
            request.AutopilotProfiles.Count,
            !string.IsNullOrWhiteSpace(request.ExpertDeployConfigurationJson));
        ReportProgress(request.Progress, 80, "Provisioning embedded assets.");
        WinPeResult assetProvisioning = await _mountedImageAssetProvisioningService.ProvisionAsync(
            session.MountDirectoryPath,
            request.Artifact.Architecture,
            request.FoundryConnectConfigurationJson,
            request.FoundryConnectAssetFiles,
            request.ExpertDeployConfigurationJson,
            request.AutopilotProfiles,
            cancellationToken).ConfigureAwait(false);
        if (!assetProvisioning.IsSuccess)
        {
            return await FailWithDiscardAsync(assetProvisioning.Error!, session, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Mounted image asset provisioning completed successfully. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        ReportProgress(request.Progress, 92, "Committing image changes.");
        _logger.LogInformation("Committing mounted WinPE image changes. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        WinPeResult commit = await session.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (commit.IsSuccess)
        {
            _logger.LogInformation("Committed mounted WinPE image changes successfully. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
            ReportProgress(request.Progress, 100, "Image customization completed.");
        }

        return commit;
    }

    private WinPeResult ApplyWinReWifiAdjustments(
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
                _logger.LogInformation("Removed winpeshl.ini from mounted WinRE-based boot image. FilePath={FilePath}", winPeShellPath);
            }
            else
            {
                _logger.LogDebug("winpeshl.ini was not present in mounted WinRE-based boot image. FilePath={FilePath}", winPeShellPath);
            }

            foreach (WinReDependencyFile dependencyFile in preparationResult.DependencyFiles)
            {
                if (!File.Exists(dependencyFile.StagedPath))
                {
                    return WinPeResult.Failure(
                        WinPeErrorCodes.WinReExtractionFailed,
                        $"The staged wireless dependency '{dependencyFile.FileName}' is missing.",
                        $"Expected path: '{dependencyFile.StagedPath}'.");
                }

                string destinationPath = Path.Combine(system32Path, dependencyFile.FileName);
                File.Copy(dependencyFile.StagedPath, destinationPath, overwrite: true);
                _logger.LogDebug(
                    "Copied WinRE wireless dependency into mounted boot image. FileName={FileName}, SourcePath={SourcePath}, DestinationPath={DestinationPath}",
                    dependencyFile.FileName,
                    dependencyFile.StagedPath,
                    destinationPath);
            }

            _logger.LogInformation(
                "Applied WinRE Wi-Fi adjustments to mounted boot image. MountDirectoryPath={MountDirectoryPath}, DependencyFileCount={DependencyFileCount}",
                mountedImagePath,
                preparationResult.DependencyFiles.Count);
            return WinPeResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply WinRE Wi-Fi adjustments to mounted boot image. MountDirectoryPath={MountDirectoryPath}", mountedImagePath);
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to apply WinRE Wi-Fi startup fixes to the mounted boot image.",
                ex.ToString());
        }
    }

    private async Task<WinPeResult> InjectDriversAsync(
        string mountedImagePath,
        IReadOnlyList<string> driverDirectories,
        string dismPath,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        if (driverDirectories.Count == 0)
        {
            _logger.LogInformation("Skipping driver injection because no driver directories were resolved. MountDirectoryPath={MountDirectoryPath}", mountedImagePath);
            return WinPeResult.Success();
        }

        _logger.LogInformation(
            "Starting driver injection into mounted WinPE image. DriverDirectoryCount={DriverDirectoryCount}, MountDirectoryPath={MountDirectoryPath}",
            driverDirectories.Count,
            mountedImagePath);
        WinPeResult inject = await _driverInjectionService.InjectAsync(new WinPeDriverInjectionOptions
        {
            MountedImagePath = mountedImagePath,
            DriverPackagePaths = driverDirectories,
            RecurseSubdirectories = true,
            DismExecutablePath = dismPath,
            WorkingDirectoryPath = workingDirectoryPath
        }, cancellationToken).ConfigureAwait(false);

        if (inject.IsSuccess)
        {
            _logger.LogInformation("Driver injection completed for mounted WinPE image. MountDirectoryPath={MountDirectoryPath}", mountedImagePath);
        }

        return inject;
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

    private static IProgress<WinPeMountedImageCustomizationProgress>? CreateNestedProgress(
        IProgress<WinPeMountedImageCustomizationProgress>? parent,
        int startPercent,
        int endPercent)
    {
        if (parent is null)
        {
            return null;
        }

        int start = Math.Clamp(startPercent, 0, 100);
        int end = Math.Clamp(endPercent, start, 100);
        int range = end - start;

        return new Progress<WinPeMountedImageCustomizationProgress>(update =>
        {
            int normalizedPercent = Math.Clamp(update.Percent, 0, 100);
            int nestedPercent = start;
            if (range > 0)
            {
                nestedPercent += (int)Math.Round(range * (normalizedPercent / 100d), MidpointRounding.AwayFromZero);
            }

            parent.Report(new WinPeMountedImageCustomizationProgress
            {
                Percent = nestedPercent,
                Status = update.Status
            });
        });
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
