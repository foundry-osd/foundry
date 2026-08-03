// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Describes the PowerShell modules to integrate into a boot image during customization.
/// </summary>
public sealed record WinPePowerShellModuleSettings
{
    /// <summary>
    /// Gets the modules to integrate.
    /// </summary>
    public IReadOnlyList<PowerShellModuleSelection> Modules { get; init; } = [];

    /// <summary>
    /// Gets the directory used to cache downloaded module packages.
    /// </summary>
    public string CacheDirectoryPath { get; init; } = string.Empty;
}
