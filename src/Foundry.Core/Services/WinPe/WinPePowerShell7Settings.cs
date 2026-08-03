// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Describes whether and how PowerShell 7 is integrated into a boot image during customization.
/// </summary>
public sealed record WinPePowerShell7Settings
{
    /// <summary>
    /// Gets a value indicating whether PowerShell 7 integration is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the selected PowerShell 7 release to integrate.
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
}
