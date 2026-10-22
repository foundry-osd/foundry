// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Globalization;

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Describes the high-level workspace preparation flow before ISO or USB media is created.
/// </summary>
public sealed record WinPeWorkspacePreparationOptions
{
    /// <summary>Gets the persistent original driver archive cache supplied by the authoring host.</summary>
    public string? DriverArchiveCacheDirectoryPath { get; init; }

    /// <summary>
    /// Gets the build artifact created by <see cref="IWinPeBuildService"/>.
    /// </summary>
    public WinPeBuildArtifact? Artifact { get; init; }

    /// <summary>
    /// Gets the ADK tool paths.
    /// </summary>
    public WinPeToolPaths? Tools { get; init; }

    /// <summary>
    /// Gets the requested signature mode for boot media.
    /// </summary>
    public WinPeSignatureMode SignatureMode { get; init; } = WinPeSignatureMode.Pca2011;

    /// <summary>
    /// Gets whether the media adds Wi-Fi support: wireless dependencies and the Intel wireless driver supplement.
    /// </summary>
    public bool IncludeWirelessSupport { get; init; }

    /// <summary>
    /// Gets the driver catalog URI.
    /// </summary>
    public string DriverCatalogUri { get; init; } = string.Empty;

    /// <summary>
    /// Gets selected driver vendors.
    /// </summary>
    public IReadOnlyList<WinPeVendorSelection> DriverVendors { get; init; } = [];

    /// <summary>
    /// Gets an optional custom driver directory.
    /// </summary>
    public string? CustomDriverDirectoryPath { get; init; }

    /// <summary>
    /// Gets the WinPE language selected for the boot image.
    /// </summary>
    public string WinPeLanguage { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional keyboard layout applied to the boot image; null follows the WinPE language default.
    /// </summary>
    public KeyboardLayout? WinPeKeyboardLayout { get; init; }

    /// <summary>
    /// Gets optional asset provisioning settings.
    /// </summary>
    public WinPeMountedImageAssetProvisioningOptions? AssetProvisioning { get; init; }

    /// <summary>
    /// Gets optional runtime payload provisioning settings.
    /// </summary>
    public WinPeRuntimePayloadProvisioningOptions? RuntimePayloadProvisioning { get; init; }

    /// <summary>
    /// Gets the directory that holds Windows source package downloads until their WinRE is cached.
    /// </summary>
    public string WindowsSourceCacheDirectoryPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the directory of extracted WinRE entries reused across builds.
    /// </summary>
    public string WinReCacheDirectoryPath { get; init; } = string.Empty;

    /// <summary>Gets an obsolete source download directory emptied after a successful WinRE extraction.</summary>
    public string? LegacyWindowsSourceDirectoryPath { get; init; }

    /// <summary>
    /// Gets the optional operating system catalog URI used to resolve the WinRE source.
    /// </summary>
    public Uri? WinReCatalogUri { get; init; }

    /// <summary>
    /// Gets the high-level workspace preparation progress receiver.
    /// </summary>
    public IProgress<WinPeWorkspacePreparationStage>? Progress { get; init; }

    /// <summary>
    /// Gets the package download progress receiver.
    /// </summary>
    public IProgress<WinPeDownloadProgress>? DownloadProgress { get; init; }

    /// <summary>
    /// Gets the mounted image customization progress receiver.
    /// </summary>
    public IProgress<WinPeMountedImageCustomizationProgress>? CustomizationProgress { get; init; }
}
