// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Describes the PowerShell modules to integrate into a mounted WinPE boot image.
/// </summary>
public sealed record WinPePowerShellModuleProvisioningOptions
{
    /// <summary>
    /// Gets the mounted boot image root.
    /// </summary>
    public string MountedImagePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the modules to integrate.
    /// </summary>
    public IReadOnlyList<PowerShellModuleSelection> Modules { get; init; } = [];

    /// <summary>
    /// Gets the directory used to cache downloaded module packages.
    /// </summary>
    public string CacheDirectoryPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the base URI of the PowerShell Gallery package endpoint.
    /// </summary>
    public string GalleryBaseUri { get; init; } = "https://www.powershellgallery.com/api/v2/package";

    /// <summary>
    /// Gets the optional download progress callback.
    /// </summary>
    public IProgress<WinPeDownloadProgress>? DownloadProgress { get; init; }
}
