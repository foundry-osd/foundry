// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Describes a stable PowerShell 7 release and its architecture-specific ZIP asset.
/// </summary>
public sealed record PowerShell7Release
{
    /// <summary>
    /// Gets the release version without the leading tag prefix, such as <c>7.4.17</c>.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Gets the Git tag for the release, such as <c>v7.4.17</c>.
    /// </summary>
    public required string Tag { get; init; }

    /// <summary>
    /// Gets the direct download URL for the architecture-specific ZIP asset.
    /// </summary>
    public required string DownloadUrl { get; init; }

    /// <summary>
    /// Gets the ZIP asset file name, such as <c>PowerShell-7.4.17-win-x64.zip</c>.
    /// </summary>
    public required string AssetName { get; init; }
}
