using Microsoft.Extensions.Logging;

namespace Foundry.Services.WinPe;

internal sealed class WinPeMountedImageCustomizationService : IWinPeMountedImageCustomizationService
{
    private readonly IWinPeDriverInjectionService _driverInjectionService;
    private readonly IWinPeImageInternationalizationService _imageInternationalizationService;
    private readonly IWinPeLocalDeployEmbeddingService _localDeployEmbeddingService;
    private readonly IWinPeMountedImageAssetProvisioningService _mountedImageAssetProvisioningService;
    private readonly WinPeProcessRunner _processRunner;
    private readonly ILogger<WinPeMountedImageCustomizationService> _logger;

    public WinPeMountedImageCustomizationService(
        IWinPeDriverInjectionService driverInjectionService,
        IWinPeImageInternationalizationService imageInternationalizationService,
        IWinPeLocalDeployEmbeddingService localDeployEmbeddingService,
        IWinPeMountedImageAssetProvisioningService mountedImageAssetProvisioningService,
        WinPeProcessRunner processRunner,
        ILogger<WinPeMountedImageCustomizationService> logger)
    {
        _driverInjectionService = driverInjectionService;
        _imageInternationalizationService = imageInternationalizationService;
        _localDeployEmbeddingService = localDeployEmbeddingService;
        _mountedImageAssetProvisioningService = mountedImageAssetProvisioningService;
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<WinPeResult> CustomizeAsync(
        WinPeMountedImageCustomizationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string normalizedLocale = _imageInternationalizationService.NormalizeWinPeLanguageCode(request.WinPeLanguage);
        if (!_imageInternationalizationService.TryResolveInputLocale(normalizedLocale, out _, out _))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.ValidationFailed,
                "Unable to resolve keyboard layout from selected WinPE language.",
                $"Selected language: '{normalizedLocale}'.");
        }

        _logger.LogInformation(
            "Mounting WinPE image for customization. BootWimPath={BootWimPath}, MountDirectoryPath={MountDirectoryPath}, WinPeLanguage={WinPeLanguage}",
            request.Artifact.BootWimPath,
            request.Artifact.MountDirectoryPath,
            normalizedLocale);
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

        WinPeResult internationalizationResult = await _imageInternationalizationService.ApplyAsync(
            session.MountDirectoryPath,
            request.Artifact.Architecture,
            request.Tools,
            normalizedLocale,
            request.Artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!internationalizationResult.IsSuccess)
        {
            return await FailWithDiscardAsync(internationalizationResult.Error!, session, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Applied WinPE international settings successfully. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);

        WinPeResult localDeployProvisioning = await _localDeployEmbeddingService.ProvisionAsync(
            session.MountDirectoryPath,
            request.Artifact.Architecture,
            request.Artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!localDeployProvisioning.IsSuccess)
        {
            return await FailWithDiscardAsync(localDeployProvisioning.Error!, session, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Provisioned local Foundry.Deploy archive into mounted WinPE image. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        WinPeResult assetProvisioning = await _mountedImageAssetProvisioningService.ProvisionAsync(
            session.MountDirectoryPath,
            request.Artifact.Architecture,
            request.ExpertDeployConfigurationJson,
            cancellationToken).ConfigureAwait(false);
        if (!assetProvisioning.IsSuccess)
        {
            return await FailWithDiscardAsync(assetProvisioning.Error!, session, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Committing mounted WinPE image changes. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        WinPeResult commit = await session.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (commit.IsSuccess)
        {
            _logger.LogInformation("Committed mounted WinPE image changes successfully. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        }

        return commit;
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
}
