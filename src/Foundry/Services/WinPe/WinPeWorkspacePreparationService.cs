using Microsoft.Extensions.Logging;

namespace Foundry.Services.WinPe;

internal sealed class WinPeWorkspacePreparationService : IWinPeWorkspacePreparationService
{
    private readonly IWinPeDriverResolutionService _driverResolutionService;
    private readonly IWinPeMountedImageCustomizationService _mountedImageCustomizationService;
    private readonly WinPeToolResolver _toolResolver;
    private readonly WinPeProcessRunner _processRunner;
    private readonly ILogger<WinPeWorkspacePreparationService> _logger;

    public WinPeWorkspacePreparationService(
        IWinPeDriverResolutionService driverResolutionService,
        IWinPeMountedImageCustomizationService mountedImageCustomizationService,
        WinPeToolResolver toolResolver,
        WinPeProcessRunner processRunner,
        ILogger<WinPeWorkspacePreparationService> logger)
    {
        _driverResolutionService = driverResolutionService;
        _mountedImageCustomizationService = mountedImageCustomizationService;
        _toolResolver = toolResolver;
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<WinPeResult<WinPeWorkspacePreparationResult>> PrepareAsync(
        WinPeWorkspacePreparationRequest request,
        IProgress<WinPeWorkspacePreparationStage>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        progress?.Report(WinPeWorkspacePreparationStage.ResolvingDrivers);
        WinPeResult<IReadOnlyList<string>> drivers = await _driverResolutionService.ResolveAsync(
            new WinPeDriverResolutionRequest
            {
                CatalogUri = request.DriverCatalogUri,
                Architecture = request.Artifact.Architecture,
                DriverVendors = request.DriverVendors,
                CustomDriverDirectoryPath = request.CustomDriverDirectoryPath,
                Artifact = request.Artifact
            },
            cancellationToken).ConfigureAwait(false);
        if (!drivers.IsSuccess)
        {
            return WinPeResult<WinPeWorkspacePreparationResult>.Failure(drivers.Error!);
        }

        _logger.LogInformation(
            "Resolved {DriverDirectoryCount} driver directory path(s). WorkingDirectoryPath={WorkingDirectoryPath}",
            drivers.Value!.Count,
            request.Artifact.WorkingDirectoryPath);

        progress?.Report(WinPeWorkspacePreparationStage.CustomizingImage);
        _logger.LogInformation(
            "Starting WinPE image customization. WorkingDirectoryPath={WorkingDirectoryPath}, DriverDirectoryCount={DriverDirectoryCount}, WinPeLanguage={WinPeLanguage}",
            request.Artifact.WorkingDirectoryPath,
            drivers.Value!.Count,
            request.WinPeLanguage);
        WinPeResult customize = await _mountedImageCustomizationService.CustomizeAsync(
            new WinPeMountedImageCustomizationRequest
            {
                Artifact = request.Artifact,
                Tools = request.Tools,
                DriverDirectories = drivers.Value!,
                BootImageSource = request.BootImageSource,
                WinPeLanguage = request.WinPeLanguage,
                ExpertDeployConfigurationJson = request.ExpertDeployConfigurationJson,
                AutopilotProfiles = request.AutopilotProfiles
            },
            cancellationToken).ConfigureAwait(false);
        if (!customize.IsSuccess)
        {
            return WinPeResult<WinPeWorkspacePreparationResult>.Failure(customize.Error!);
        }

        _logger.LogInformation(
            "WinPE image customization completed. WorkingDirectoryPath={WorkingDirectoryPath}",
            request.Artifact.WorkingDirectoryPath);

        progress?.Report(WinPeWorkspacePreparationStage.EvaluatingSignaturePolicy);
        bool useBootEx = false;
        if (request.SignatureMode == WinPeSignatureMode.Pca2023)
        {
            _logger.LogInformation(
                "Evaluating PCA2023 signature policy. WorkingDirectoryPath={WorkingDirectoryPath}",
                request.Artifact.WorkingDirectoryPath);
            useBootEx = await _toolResolver.IsBootExSupportedAsync(
                request.Tools,
                _processRunner,
                request.Artifact.WorkingDirectoryPath,
                cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("PCA2023 signature policy evaluated. BootExSupported={BootExSupported}", useBootEx);
            if (!useBootEx)
            {
                return WinPeResult<WinPeWorkspacePreparationResult>.Failure(
                    new WinPeDiagnostic(
                        WinPeErrorCodes.BootExUnsupported,
                        "PCA2023 requires /bootex support in the WinPE workspace."));
            }
        }

        return WinPeResult<WinPeWorkspacePreparationResult>.Success(new WinPeWorkspacePreparationResult
        {
            UseBootEx = useBootEx
        });
    }

}
