// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Identifies the Windows source package a cached WinRE was extracted from.
/// </summary>
/// <param name="Architecture">Boot image architecture the entry serves.</param>
/// <param name="Language">Windows source language the entry serves.</param>
/// <param name="BuildMajor">Source build number, for example 26300.</param>
/// <param name="BuildUbr">Source update build revision.</param>
/// <param name="Edition">Edition exported from the source package.</param>
/// <param name="SourceFileName">Catalog file name of the source package.</param>
/// <param name="SourceSha256">Catalog SHA-256 of the source package; part of the entry key.</param>
internal sealed record WinReCacheSource(
    WinPeArchitecture Architecture,
    string Language,
    int BuildMajor,
    int BuildUbr,
    string Edition,
    string SourceFileName,
    string SourceSha256);

/// <summary>
/// Describes one published WinRE cache entry; it is written last so a present manifest marks a complete entry.
/// </summary>
internal sealed record WinReCacheManifest
{
    public int Version { get; init; } = 1;
    public required string Architecture { get; init; }
    public required string Language { get; init; }
    public required int BuildMajor { get; init; }
    public required int BuildUbr { get; init; }
    public required string Edition { get; init; }
    public required string SourceFileName { get; init; }
    public required string SourceSha256 { get; init; }
    public required IReadOnlyList<WinReCacheFile> Files { get; init; }
}

/// <summary>
/// Records the expected length and SHA-256 of one cached file, verified before every reuse.
/// </summary>
internal sealed record WinReCacheFile(string Name, long Length, string Sha256);

/// <summary>
/// A verified WinRE cache entry.
/// </summary>
internal sealed record WinReCacheEntry(string DirectoryPath, WinReCacheManifest Manifest)
{
    /// <summary>Gets the path of a cached file by its manifest name.</summary>
    public string GetFilePath(string name) => Path.Combine(DirectoryPath, name);
}
