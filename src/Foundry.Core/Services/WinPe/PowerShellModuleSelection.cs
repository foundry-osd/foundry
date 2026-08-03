// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Identifies where a PowerShell module to be integrated comes from.
/// </summary>
public enum PowerShellModuleSource
{
    /// <summary>
    /// The module is downloaded from the PowerShell Gallery.
    /// </summary>
    Gallery,

    /// <summary>
    /// The module is copied from a local folder.
    /// </summary>
    Local
}

/// <summary>
/// Describes a single PowerShell module to integrate into a boot image.
/// </summary>
public sealed record PowerShellModuleSelection
{
    /// <summary>
    /// Gets where the module comes from.
    /// </summary>
    public PowerShellModuleSource Source { get; init; }

    /// <summary>
    /// Gets the module name (used for the Gallery and as the destination folder name).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Gallery module version to download. Required for <see cref="PowerShellModuleSource.Gallery"/>.
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// Gets the local module folder to copy. Required for <see cref="PowerShellModuleSource.Local"/>.
    /// </summary>
    public string LocalPath { get; init; } = string.Empty;
}
