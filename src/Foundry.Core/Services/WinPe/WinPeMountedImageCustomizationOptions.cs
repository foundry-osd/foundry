namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Describes all inputs required to customize a mounted WinPE or WinRE boot image.
/// </summary>
public sealed record WinPeMountedImageCustomizationOptions
{
    /// <summary>
    /// Gets the WinPE build artifact containing workspace paths.
    /// </summary>
    public WinPeBuildArtifact? Artifact { get; init; }

    /// <summary>
    /// Gets ADK tool paths used during customization.
    /// </summary>
    public WinPeToolPaths? Tools { get; init; }

    /// <summary>
    /// Gets whether customization targets WinPE boot.wim or a prepared WinRE image.
    /// </summary>
    public WinPeBootImageSource BootImageSource { get; init; } = WinPeBootImageSource.WinPe;

    /// <summary>
    /// Gets the WinPE language pack to apply.
    /// </summary>
    public string WinPeLanguage { get; init; } = string.Empty;

    /// <summary>
    /// Gets driver package directories to inject.
    /// </summary>
    public IReadOnlyList<string> DriverPackagePaths { get; init; } = [];

    /// <summary>
    /// Gets optional asset provisioning settings.
    /// </summary>
    public WinPeMountedImageAssetProvisioningOptions? AssetProvisioning { get; init; }

    /// <summary>
    /// Gets optional runtime payload provisioning settings for Foundry.Connect and Foundry.Deploy.
    /// </summary>
    public WinPeRuntimePayloadProvisioningOptions? RuntimePayloadProvisioning { get; init; }

    /// <summary>
    /// Gets the cache path used for WinRE downloads.
    /// </summary>
    public string WinReCacheDirectoryPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional WinRE catalog URI.
    /// </summary>
    public Uri? WinReCatalogUri { get; init; }

    /// <summary>
    /// Gets download progress callbacks for WinRE and driver artifacts.
    /// </summary>
    public IProgress<WinPeDownloadProgress>? DownloadProgress { get; init; }

    /// <summary>
    /// Gets customization progress callbacks.
    /// </summary>
    public IProgress<WinPeMountedImageCustomizationProgress>? Progress { get; init; }
}
