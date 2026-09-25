// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Models.Images;

/// <summary>Describes an immutable media generation containing only explicitly included managed images.</summary>
public sealed record CustomImageMediaManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string ManifestId { get; init; } = string.Empty;
    public IReadOnlyList<CustomImageMediaEntry> Images { get; init; } = [];
}

/// <summary>Uses volume-relative paths; consumers must validate containment before opening content.</summary>
public sealed record CustomImageMediaEntry
{
    public CustomImageReference Reference { get; init; } = new();
    public string RelativePath { get; init; } = string.Empty;
    public string? SourceRelativePath { get; init; }
    public IReadOnlyList<CustomImageSourceFile> SourceFiles { get; init; } = [];
}

/// <summary>Binds one optional-feature source file to its immutable bytes.</summary>
public sealed record CustomImageSourceFile
{
    public string RelativePath { get; init; } = string.Empty;
    public long Length { get; init; }
    public string ContentHash { get; init; } = string.Empty;
}
