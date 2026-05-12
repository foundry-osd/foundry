namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Coordinates driver resolution, boot image customization, and signature policy validation for a WinPE workspace.
/// </summary>
public sealed class WinPeWorkspacePreparationService : IWinPeWorkspacePreparationService
{
    private readonly IWinPeDriverResolutionService _driverResolutionService;
    private readonly IWinPeMountedImageCustomizationService _mountedImageCustomizationService;
    private readonly WinPeToolResolver _toolResolver;
    private readonly IWinPeProcessRunner _processRunner;

    /// <summary>
    /// Initializes a workspace preparation service using the default WinPE services.
    /// </summary>
    public WinPeWorkspacePreparationService()
        : this(
            new WinPeDriverResolutionService(new WinPeDriverCatalogService(), new WinPeDriverPackageService()),
            new WinPeMountedImageCustomizationService(),
            new WinPeToolResolver(),
            new WinPeProcessRunner())
    {
    }

    internal WinPeWorkspacePreparationService(
        IWinPeDriverResolutionService driverResolutionService,
        IWinPeMountedImageCustomizationService mountedImageCustomizationService,
        WinPeToolResolver toolResolver,
        IWinPeProcessRunner processRunner)
    {
        _driverResolutionService = driverResolutionService;
        _mountedImageCustomizationService = mountedImageCustomizationService;
        _toolResolver = toolResolver;
        _processRunner = processRunner;
    }

    /// <inheritdoc />
    public async Task<WinPeResult<WinPeWorkspacePreparationResult>> PrepareAsync(
        WinPeWorkspacePreparationOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WinPeDiagnostic? validationError = ValidateOptions(options);
        if (validationError is not null)
        {
            return WinPeResult<WinPeWorkspacePreparationResult>.Failure(validationError);
        }

        WinPeBuildArtifact artifact = options.Artifact!;
        WinPeToolPaths tools = options.Tools!;

        options.Progress?.Report(WinPeWorkspacePreparationStage.ResolvingDrivers);
        WinPeResult<IReadOnlyList<string>> drivers = await _driverResolutionService.ResolveAsync(
            new WinPeDriverResolutionRequest
            {
                CatalogUri = options.DriverCatalogUri,
                Architecture = artifact.Architecture,
                BootImageSource = options.BootImageSource,
                DriverVendors = options.DriverVendors,
                CustomDriverDirectoryPath = options.CustomDriverDirectoryPath,
                Artifact = artifact,
                DownloadProgress = options.DownloadProgress
            },
                cancellationToken).ConfigureAwait(false);

        if (!drivers.IsSuccess)
        {
            return WinPeResult<WinPeWorkspacePreparationResult>.Failure(drivers.Error!);
        }

        options.Progress?.Report(WinPeWorkspacePreparationStage.CustomizingImage);
        WinPeResult customization = await _mountedImageCustomizationService.CustomizeAsync(
            new WinPeMountedImageCustomizationOptions
            {
                Artifact = artifact,
                Tools = tools,
                BootImageSource = options.BootImageSource,
                WinPeLanguage = options.WinPeLanguage,
                DriverPackagePaths = drivers.Value!,
                AssetProvisioning = options.AssetProvisioning,
                RuntimePayloadProvisioning = options.RuntimePayloadProvisioning,
                WinReCacheDirectoryPath = options.WinReCacheDirectoryPath,
                WinReCatalogUri = options.WinReCatalogUri,
                DownloadProgress = options.DownloadProgress,
                Progress = options.CustomizationProgress
            },
            cancellationToken).ConfigureAwait(false);

        if (!customization.IsSuccess)
        {
            return WinPeResult<WinPeWorkspacePreparationResult>.Failure(customization.Error!);
        }

        options.Progress?.Report(WinPeWorkspacePreparationStage.EvaluatingSignaturePolicy);
        bool useBootEx = false;
        if (options.SignatureMode == WinPeSignatureMode.Pca2023)
        {
            // PCA2023 boot media requires a newer ADK MakeWinPEMedia implementation with /bootex support.
            useBootEx = await _toolResolver.IsBootExSupportedAsync(
                tools,
                _processRunner,
                artifact.WorkingDirectoryPath,
                cancellationToken).ConfigureAwait(false);

            if (!useBootEx)
            {
                return WinPeResult<WinPeWorkspacePreparationResult>.Failure(
                    WinPeErrorCodes.BootExUnsupported,
                    "PCA2023 requires /bootex support in the WinPE workspace.",
                    $"MakeWinPEMedia path: '{tools.MakeWinPeMediaPath}'.");
            }
        }

        return WinPeResult<WinPeWorkspacePreparationResult>.Success(new WinPeWorkspacePreparationResult
        {
            Artifact = artifact,
            Tools = tools,
            UseBootEx = useBootEx
        });
    }

    private static WinPeDiagnostic? ValidateOptions(WinPeWorkspacePreparationOptions? options)
    {
        if (options is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "WinPE workspace preparation options are required.",
                "Provide a non-null WinPeWorkspacePreparationOptions instance.");
        }

        if (options.Artifact is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "WinPE build artifact is required.",
                "Set WinPeWorkspacePreparationOptions.Artifact.");
        }

        if (options.Tools is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "WinPE tool paths are required.",
                "Set WinPeWorkspacePreparationOptions.Tools.");
        }

        if (!Enum.IsDefined(options.SignatureMode))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Signature mode value is invalid.",
                $"Value: '{options.SignatureMode}'.");
        }

        if (!Enum.IsDefined(options.BootImageSource))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Boot image source value is invalid.",
                $"Value: '{options.BootImageSource}'.");
        }

        if (string.IsNullOrWhiteSpace(options.DriverCatalogUri))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Driver catalog URI is required.",
                "Set WinPeWorkspacePreparationOptions.DriverCatalogUri.");
        }

        if (string.IsNullOrWhiteSpace(options.WinPeLanguage))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "WinPE language is required.",
                "Set WinPeWorkspacePreparationOptions.WinPeLanguage.");
        }

        return null;
    }
}
