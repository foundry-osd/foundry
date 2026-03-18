using Microsoft.Extensions.Logging;

namespace Foundry.Services.WinPe;

internal sealed class WinPeWorkspacePreparationService : IWinPeWorkspacePreparationService
{
    private readonly IWinPeDriverCatalogService _driverCatalogService;
    private readonly IWinPeDriverInjectionService _driverInjectionService;
    private readonly WinPeDriverPackageService _driverPackageService;
    private readonly IWinPeImageInternationalizationService _imageInternationalizationService;
    private readonly IWinPeLocalDeployEmbeddingService _localDeployEmbeddingService;
    private readonly IWinPeMountedImageAssetProvisioningService _mountedImageAssetProvisioningService;
    private readonly WinPeToolResolver _toolResolver;
    private readonly WinPeProcessRunner _processRunner;
    private readonly ILogger<WinPeWorkspacePreparationService> _logger;

    public WinPeWorkspacePreparationService(
        IWinPeDriverCatalogService driverCatalogService,
        IWinPeDriverInjectionService driverInjectionService,
        WinPeDriverPackageService driverPackageService,
        IWinPeImageInternationalizationService imageInternationalizationService,
        IWinPeLocalDeployEmbeddingService localDeployEmbeddingService,
        IWinPeMountedImageAssetProvisioningService mountedImageAssetProvisioningService,
        WinPeToolResolver toolResolver,
        WinPeProcessRunner processRunner,
        ILogger<WinPeWorkspacePreparationService> logger)
    {
        _driverCatalogService = driverCatalogService;
        _driverInjectionService = driverInjectionService;
        _driverPackageService = driverPackageService;
        _imageInternationalizationService = imageInternationalizationService;
        _localDeployEmbeddingService = localDeployEmbeddingService;
        _mountedImageAssetProvisioningService = mountedImageAssetProvisioningService;
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
        WinPeResult<IReadOnlyList<string>> drivers = await ResolveDriversAsync(
            request.DriverCatalogUri,
            request.Artifact.Architecture,
            request.DriverVendors,
            request.CustomDriverDirectoryPath,
            request.Artifact,
            request.Tools,
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
        WinPeResult customize = await CustomizeImageAsync(
            request.Artifact,
            request.Tools,
            drivers.Value!,
            request.WinPeLanguage,
            request.ExpertDeployConfigurationJson,
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

    private async Task<WinPeResult<IReadOnlyList<string>>> ResolveDriversAsync(
        string catalogUri,
        WinPeArchitecture architecture,
        IReadOnlyList<WinPeVendorSelection> driverVendors,
        string? customDriverDirectoryPath,
        WinPeBuildArtifact artifact,
        WinPeToolPaths tools,
        CancellationToken cancellationToken)
    {
        WinPeVendorSelection[] normalizedVendors = driverVendors
            .Where(vendor => vendor != WinPeVendorSelection.Any)
            .Distinct()
            .ToArray();

        string normalizedCustomDirectory = customDriverDirectoryPath?.Trim() ?? string.Empty;
        bool hasCustomDirectory = !string.IsNullOrWhiteSpace(normalizedCustomDirectory);

        if (normalizedVendors.Length == 0 && !hasCustomDirectory)
        {
            return WinPeResult<IReadOnlyList<string>>.Success(Array.Empty<string>());
        }

        if (hasCustomDirectory)
        {
            if (!Directory.Exists(normalizedCustomDirectory))
            {
                return WinPeResult<IReadOnlyList<string>>.Failure(
                    WinPeErrorCodes.ValidationFailed,
                    "Custom driver directory does not exist.",
                    $"Path: '{normalizedCustomDirectory}'.");
            }

            bool hasInf = Directory.EnumerateFiles(normalizedCustomDirectory, "*.inf", SearchOption.AllDirectories).Any();
            if (!hasInf)
            {
                return WinPeResult<IReadOnlyList<string>>.Failure(
                    WinPeErrorCodes.ValidationFailed,
                    "Custom driver directory does not contain any .inf files.",
                    $"Path: '{normalizedCustomDirectory}'.");
            }
        }

        var resolvedPaths = new List<string>();

        if (normalizedVendors.Length > 0)
        {
            WinPeResult<IReadOnlyList<WinPeDriverCatalogEntry>> catalog = await _driverCatalogService.GetCatalogAsync(new WinPeDriverCatalogOptions
            {
                CatalogUri = catalogUri,
                Architecture = architecture,
                Vendors = normalizedVendors
            }, cancellationToken).ConfigureAwait(false);

            if (!catalog.IsSuccess)
            {
                return WinPeResult<IReadOnlyList<string>>.Failure(catalog.Error!);
            }

            WinPeDriverCatalogEntry[] selectedPackages = catalog.Value?
                .GroupBy(item => item.Vendor)
                .Select(group => group
                    .OrderByDescending(item => item.ReleaseDate ?? DateTimeOffset.MinValue)
                    .First())
                .ToArray() ?? [];

            if (selectedPackages.Length > 0)
            {
                WinPeResult<WinPePreparedDriverSet> prepared = await _driverPackageService.PrepareAsync(
                    selectedPackages,
                    Path.Combine(artifact.DriverWorkspacePath, "downloads"),
                    Path.Combine(artifact.DriverWorkspacePath, "extracted"),
                    cancellationToken).ConfigureAwait(false);
                if (!prepared.IsSuccess)
                {
                    return WinPeResult<IReadOnlyList<string>>.Failure(prepared.Error!);
                }

                resolvedPaths.AddRange(prepared.Value!.ExtractionDirectories);
            }
        }

        if (hasCustomDirectory)
        {
            resolvedPaths.Add(normalizedCustomDirectory);
        }

        return WinPeResult<IReadOnlyList<string>>.Success(
            resolvedPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }
    private async Task<WinPeResult> CustomizeImageAsync(
        WinPeBuildArtifact artifact,
        WinPeToolPaths tools,
        IReadOnlyList<string> driverDirectories,
        string winPeLanguage,
        string? expertDeployConfigurationJson,
        CancellationToken cancellationToken)
    {
        string normalizedLocale = _imageInternationalizationService.NormalizeWinPeLanguageCode(winPeLanguage);
        if (!_imageInternationalizationService.TryResolveInputLocale(normalizedLocale, out _, out _))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.ValidationFailed,
                "Unable to resolve keyboard layout from selected WinPE language.",
                $"Selected language: '{normalizedLocale}'.");
        }

        _logger.LogInformation(
            "Mounting WinPE image for customization. BootWimPath={BootWimPath}, MountDirectoryPath={MountDirectoryPath}, WinPeLanguage={WinPeLanguage}",
            artifact.BootWimPath,
            artifact.MountDirectoryPath,
            normalizedLocale);
        WinPeResult<WinPeMountSession> mount = await WinPeMountSession.MountAsync(
            _processRunner,
            tools.DismPath,
            artifact.BootWimPath,
            artifact.MountDirectoryPath,
            artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!mount.IsSuccess)
        {
            return WinPeResult.Failure(mount.Error!);
        }

        await using WinPeMountSession session = mount.Value!;
        _logger.LogInformation("Mounted WinPE image for customization. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        if (driverDirectories.Count > 0)
        {
            _logger.LogInformation(
                "Starting driver injection into mounted WinPE image. DriverDirectoryCount={DriverDirectoryCount}, MountDirectoryPath={MountDirectoryPath}",
                driverDirectories.Count,
                session.MountDirectoryPath);
            WinPeResult inject = await _driverInjectionService.InjectAsync(new WinPeDriverInjectionOptions
            {
                MountedImagePath = session.MountDirectoryPath,
                DriverPackagePaths = driverDirectories,
                RecurseSubdirectories = true,
                DismExecutablePath = tools.DismPath,
                WorkingDirectoryPath = artifact.WorkingDirectoryPath
            }, cancellationToken).ConfigureAwait(false);

            if (!inject.IsSuccess)
            {
                await session.DiscardAsync(cancellationToken).ConfigureAwait(false);
                return inject;
            }

            _logger.LogInformation("Driver injection completed for mounted WinPE image. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        }
        else
        {
            _logger.LogInformation("Skipping driver injection because no driver directories were resolved. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        }

        WinPeResult internationalizationResult = await _imageInternationalizationService.ApplyAsync(
            session.MountDirectoryPath,
            artifact.Architecture,
            tools,
            normalizedLocale,
            artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!internationalizationResult.IsSuccess)
        {
            await session.DiscardAsync(cancellationToken).ConfigureAwait(false);
            return internationalizationResult;
        }

        _logger.LogInformation("Applied WinPE international settings successfully. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);

        WinPeResult localDeployProvisioning = await _localDeployEmbeddingService.ProvisionAsync(
            session.MountDirectoryPath,
            artifact.Architecture,
            artifact.WorkingDirectoryPath,
            cancellationToken).ConfigureAwait(false);
        if (!localDeployProvisioning.IsSuccess)
        {
            await session.DiscardAsync(cancellationToken).ConfigureAwait(false);
            return localDeployProvisioning;
        }

        _logger.LogInformation("Provisioned local Foundry.Deploy archive into mounted WinPE image. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        WinPeResult assetProvisioning = await _mountedImageAssetProvisioningService.ProvisionAsync(
            session.MountDirectoryPath,
            artifact.Architecture,
            expertDeployConfigurationJson,
            cancellationToken).ConfigureAwait(false);
        if (!assetProvisioning.IsSuccess)
        {
            await session.DiscardAsync(cancellationToken).ConfigureAwait(false);
            return assetProvisioning;
        }

        _logger.LogInformation("Committing mounted WinPE image changes. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        WinPeResult commit = await session.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (commit.IsSuccess)
        {
            _logger.LogInformation("Committed mounted WinPE image changes successfully. MountDirectoryPath={MountDirectoryPath}", session.MountDirectoryPath);
        }

        return commit;
    }

}
