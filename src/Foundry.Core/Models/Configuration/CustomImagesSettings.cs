// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>Chooses the deployment image workflow independently from its preferred image and numeric index.</summary>
public enum CustomImageSource { Catalog, Custom }

/// <summary>Stores portable image references; image bytes belong to the local content library and media data volume.</summary>
public sealed record CustomImagesSettings
{
    public bool IsEnabled { get; init; }
    public CustomImageSource DefaultSource { get; init; }
    public IReadOnlyList<CustomImageReference> Images { get; init; } = [];
    public string? DefaultImageId { get; init; }
    public int? DefaultImageIndex { get; init; }
}

/// <summary>Identifies immutable imported content while keeping its profile-specific label and inclusion mutable.</summary>
public sealed record CustomImageReference
{
    public string Id { get; init; } = string.Empty;
    public string ContentHash { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public long Length { get; init; }
    public IReadOnlyList<CustomImageIndex> Indexes { get; init; } = [];
    public bool IsIncluded { get; init; } = true;
}

/// <summary>Describes an exact image index without inferring Windows catalog compatibility.</summary>
public sealed record CustomImageIndex
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Architecture { get; init; } = "unknown";
    public string EditionId { get; init; } = string.Empty;
    public string ProductType { get; init; } = string.Empty;
    public int? Build { get; init; }
    public string? Version { get; init; }
    public string? DefaultLanguage { get; init; }
    public IReadOnlyList<string> Languages { get; init; } = [];
    public long ExpandedSizeBytes { get; init; }
}
