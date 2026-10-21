// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Packages;

/// <summary>Describes initial immutable source content; canonical serialization defines its package hash.</summary>
public sealed record ContentPackageManifest
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<string> Directories { get; init; } = [];
    public IReadOnlyList<ContentPackageFile> Files { get; init; } = [];
}

/// <summary>Describes one file of a content package by relative path, length and SHA-256.</summary>
public sealed record ContentPackageFile
{
    public string RelativePath { get; init; } = string.Empty;
    public long Length { get; init; }
    public string Sha256 { get; init; } = string.Empty;
}
