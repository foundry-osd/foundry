// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Packages;

namespace Foundry.Deploy.Models;

/// <summary>A custom driver set found on boot media, ready to be offered in the deployment wizard.</summary>
public sealed record CustomDriverSetOption
{
    /// <summary>Gets the stable identifier of the configured set.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the label shown in the deployment wizard.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Gets the SHA-256 of the package manifest; it identifies the package files on the media.</summary>
    public required string ContentHash { get; init; }

    /// <summary>Gets the total length of the package files in bytes.</summary>
    public required long Length { get; init; }

    /// <summary>Gets the number of files in the package.</summary>
    public required int FileCount { get; init; }

    /// <summary>Gets the number of INF files in the package.</summary>
    public required int InfCount { get; init; }

    /// <summary>Gets the root of the volume that holds the verified media manifest.</summary>
    public required string MediaRoot { get; init; }

    /// <summary>Gets the verified package manifest used to apply the set.</summary>
    public required ContentPackageManifest Manifest { get; init; }
}
