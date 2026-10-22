// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Coordinates driver resolution, WinRE boot image preparation, boot image customization, and signature policy validation for a WinPE workspace.
/// </summary>
public sealed class WinPeWorkspacePreparationService : IWinPeWorkspacePreparationService
{
    private readonly IWinPeDriverResolutionService _driverResolutionService;
    private readonly IWinPeMountedImageCustomizationService _mountedImageCustomizationService;
    private readonly IWinPeBootImagePreparationService _bootImagePreparationService;
    private readonly WinPeToolResolver _toolResolver;
    private readonly IWinPeProcessRunner _processRunner;

    /// <summary>
    /// Initializes a workspace preparation service using the default WinPE services.
    /// </summary>
    public WinPeWorkspacePreparationService()
        : this(
            new WinPeDriverResolutionService(new WinPeDriverCatalogService(), new WinPeDriverPackageService()),
            new WinPeMountedImageCustomizationService(),
            new WinPeBootImagePreparationService(),
            new WinPeToolResolver(),
            new WinPeProcessRunner())
    {
    }

    internal WinPeWorkspacePreparationService(
        IWinPeDriverResolutionService driverResolutionService,
        IWinPeMountedImageCustomizationService mountedImageCustomizationService,
        IWinPeBootImagePreparationService bootImagePreparationService,
        WinPeToolResolver toolResolver,
        IWinPeProcessRunner processRunner)
    {
        _driverResolutionService = driverResolutionService;
        _mountedImageCustomizationService = mountedImageCustomizationService;
        _bootImagePreparationService = bootImagePreparationService;
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
        WinPeResult<IReadOnlyList<string>> drivers = await ResolveDriversAsync(options, includeWirelessSupplement: false, cancellationToken).ConfigureAwait(false);
        if (!drivers.IsSuccess)
        {
            return WinPeResult<WinPeWorkspacePreparationResult>.Failure(drivers.Error!);
        }

        // The Intel Wi-Fi supplement is resolved on its own so an x64 fallback to Windows PE can leave it out.
        IReadOnlyList<string> wirelessDrivers = [];
        if (options.IncludeWirelessSupport)
        {
            WinPeResult<IReadOnlyList<string>> wireless = await ResolveDriversAsync(options, includeWirelessSupplement: true, cancellationToken).ConfigureAwait(false);
            if (!wireless.IsSuccess)
            {
                return WinPeResult<WinPeWorkspacePreparationResult>.Failure(wireless.Error!);
            }

            wirelessDrivers = wireless.Value!;
        }

        options.Progress?.Report(WinPeWorkspacePreparationStage.CustomizingImage);
        WinPeResult<WinPeBootImagePreparationResult> bootImage = await _bootImagePreparationService.PrepareAsync(
            new WinPeBootImagePreparationOptions
            {
                Artifact = artifact,
                Tools = tools,
                WinPeLanguage = options.WinPeLanguage,
                IncludeWirelessSupport = options.IncludeWirelessSupport,
                CacheDirectoryPath = options.WindowsSourceCacheDirectoryPath,
                WinReCacheDirectoryPath = options.WinReCacheDirectoryPath,
                LegacySourceDirectoryPath = options.LegacyWindowsSourceDirectoryPath,
                CatalogUri = options.WinReCatalogUri ?? WinPeBootImagePreparationService.DefaultOperatingSystemCatalogUri,
                DownloadProgress = options.DownloadProgress,
                Progress = options.CustomizationProgress
            },
            cancellationToken).ConfigureAwait(false);
        if (!bootImage.IsSuccess)
        {
            return WinPeResult<WinPeWorkspacePreparationResult>.Failure(bootImage.Error!);
        }

        WinPeBootImagePreparationResult preparedBootImage = bootImage.Value!;
        WinPeResult customization = await _mountedImageCustomizationService.CustomizeAsync(
            new WinPeMountedImageCustomizationOptions
            {
                Artifact = artifact,
                Tools = tools,
                BootImagePreparation = preparedBootImage,
                WinPeLanguage = options.WinPeLanguage,
                WinPeKeyboardLayout = options.WinPeKeyboardLayout,
                DriverPackagePaths = preparedBootImage.BootImageSource == WinPeBootImageSource.WinRe
                    ? [.. drivers.Value!, .. wirelessDrivers]
                    : drivers.Value!,
                AssetProvisioning = options.AssetProvisioning,
                RuntimePayloadProvisioning = options.RuntimePayloadProvisioning,
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
            UseBootEx = useBootEx,
            BootImageSource = preparedBootImage.BootImageSource,
            BootImageFallbackCause = preparedBootImage.FallbackCause
        });
    }

    /// <summary>
    /// Resolves either the vendor and custom drivers or, alone, the Intel wireless supplement.
    /// </summary>
    private Task<WinPeResult<IReadOnlyList<string>>> ResolveDriversAsync(
        WinPeWorkspacePreparationOptions options,
        bool includeWirelessSupplement,
        CancellationToken cancellationToken) =>
        _driverResolutionService.ResolveAsync(
            new WinPeDriverResolutionRequest
            {
                CatalogUri = options.DriverCatalogUri,
                Architecture = options.Artifact!.Architecture,
                IncludeWirelessSupplement = includeWirelessSupplement,
                DriverVendors = includeWirelessSupplement ? [] : options.DriverVendors,
                CustomDriverDirectoryPath = includeWirelessSupplement ? null : options.CustomDriverDirectoryPath,
                DriverArchiveCacheDirectoryPath = options.DriverArchiveCacheDirectoryPath,
                Artifact = options.Artifact,
                DownloadProgress = options.DownloadProgress
            },
            cancellationToken);

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

        if (string.IsNullOrWhiteSpace(options.WindowsSourceCacheDirectoryPath) || string.IsNullOrWhiteSpace(options.WinReCacheDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Windows source and WinRE cache directories are required.",
                "Set WinPeWorkspacePreparationOptions.WindowsSourceCacheDirectoryPath and WinReCacheDirectoryPath.");
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
