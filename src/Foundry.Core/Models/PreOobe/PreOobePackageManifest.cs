// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.PreOobe;

/// <summary>Describes initial immutable source content; canonical serialization defines its package hash.</summary>
public sealed record PreOobePackageManifest
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<string> Directories { get; init; } = [];
    public IReadOnlyList<PreOobePackageFile> Files { get; init; } = [];
}

public sealed record PreOobePackageFile
{
    public string RelativePath { get; init; } = string.Empty;
    public long Length { get; init; }
    public string Sha256 { get; init; } = string.Empty;
}
