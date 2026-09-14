// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Selects the Windows source and boot media behavior for dependency preparation.
/// </summary>
public sealed record WinPeBootImagePreparationOptions
{
    /// <summary>
    /// Gets the target architecture and workspace that owns staged dependencies.
    /// </summary>
    public required WinPeBuildArtifact Artifact { get; init; }
    /// <summary>
    /// Gets the DISM tools used to inspect, export, and mount the Windows source.
    /// </summary>
    public required WinPeToolPaths Tools { get; init; }
    /// <summary>
    /// Gets the boot language used to select a matching Windows source.
    /// </summary>
    public required string WinPeLanguage { get; init; }
    /// <summary>
    /// Gets the reusable Windows source package cache directory.
    /// </summary>
    public required string CacheDirectoryPath { get; init; }
    /// <summary>
    /// Gets whether source preparation also replaces the boot image with WinRE for Wi-Fi support.
    /// </summary>
    public WinPeBootImageSource BootImageSource { get; init; } = WinPeBootImageSource.WinReWifi;
    /// <summary>
    /// Gets the operating system catalog used to resolve Windows source packages.
    /// </summary>
    public Uri CatalogUri { get; init; } = WinPeBootImagePreparationService.DefaultOperatingSystemCatalogUri;
    /// <summary>
    /// Gets the optional source download progress receiver.
    /// </summary>
    public IProgress<WinPeDownloadProgress>? DownloadProgress { get; init; }
    /// <summary>
    /// Gets the optional source preparation progress receiver.
    /// </summary>
    public IProgress<WinPeMountedImageCustomizationProgress>? Progress { get; init; }
}
