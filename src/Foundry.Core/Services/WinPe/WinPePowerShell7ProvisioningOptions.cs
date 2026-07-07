// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Describes the inputs required to integrate PowerShell 7 into a mounted WinPE boot image.
/// </summary>
public sealed record WinPePowerShell7ProvisioningOptions
{
    /// <summary>
    /// Gets the mounted boot image root.
    /// </summary>
    public string MountedImagePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the target WinPE architecture.
    /// </summary>
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;

    /// <summary>
    /// Gets the PowerShell 7 release to integrate.
    /// </summary>
    public PowerShell7Release? Release { get; init; }

    /// <summary>
    /// Gets the latest release used as a non-fatal fallback when the selected release cannot be downloaded.
    /// </summary>
    public PowerShell7Release? FallbackRelease { get; init; }

    /// <summary>
    /// Gets the directory used to cache downloaded PowerShell 7 archives.
    /// </summary>
    public string CacheDirectoryPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the working directory used for reg.exe operations.
    /// </summary>
    public string WorkingDirectoryPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional download progress callback.
    /// </summary>
    public IProgress<WinPeDownloadProgress>? DownloadProgress { get; init; }
}
