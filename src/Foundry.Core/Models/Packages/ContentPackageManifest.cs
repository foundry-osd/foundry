// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Packages;

/// <summary>Describes initial immutable source content; canonical serialization defines its package hash.</summary>
public sealed record ContentPackageManifest
{
    /// <summary>Manifest format version; only 1 is supported.</summary>
    public int SchemaVersion { get; init; } = 1;
    /// <summary>Package-relative directories, including empty ones.</summary>
    public IReadOnlyList<string> Directories { get; init; } = [];
    /// <summary>Package files with their lengths and hashes.</summary>
    public IReadOnlyList<ContentPackageFile> Files { get; init; } = [];
}

/// <summary>Describes one file of a content package by relative path, length and SHA-256.</summary>
public sealed record ContentPackageFile
{
    /// <summary>Package-relative path using forward slashes.</summary>
    public string RelativePath { get; init; } = string.Empty;
    /// <summary>File length in bytes.</summary>
    public long Length { get; init; }
    /// <summary>SHA-256 of the file content as hexadecimal text.</summary>
    public string Sha256 { get; init; } = string.Empty;
}
