// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Describes a PowerShell Gallery module returned from a search.
/// </summary>
public sealed record PowerShellGalleryModule
{
    /// <summary>
    /// Gets the module id/name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the latest module version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Gets the module authors.
    /// </summary>
    public string Authors { get; init; } = string.Empty;

    /// <summary>
    /// Gets the module description.
    /// </summary>
    public string Description { get; init; } = string.Empty;
}
