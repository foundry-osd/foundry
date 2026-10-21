// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>References immutable imported content; display metadata never determines its identity.</summary>
public sealed record ContentPackageReference
{
    /// <summary>SHA-256 of the canonical package manifest; the only identity of the package.</summary>
    public string ContentHash { get; init; } = string.Empty;

    /// <summary>Name shown to the user; never used to identify the package.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Total length of the package files in bytes.</summary>
    public long Length { get; init; }

    /// <summary>Number of files in the package.</summary>
    public int FileCount { get; init; }
}
