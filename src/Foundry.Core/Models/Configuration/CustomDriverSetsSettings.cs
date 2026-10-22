// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>Stores portable custom driver set references; driver bytes belong to the local content library and the media data volume.</summary>
public sealed record CustomDriverSetsSettings
{
    /// <summary>Gets a value indicating whether custom driver sets are offered for deployment.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>Gets the driver sets of the profile, included or not.</summary>
    public IReadOnlyList<CustomDriverSetReference> Sets { get; init; } = [];

    /// <summary>Gets the identifier of the preselected set, which must be an included set, or <see langword="null"/>.</summary>
    public string? DefaultSetId { get; init; }
}

/// <summary>Identifies an immutable driver package while keeping its profile-specific label and inclusion mutable.</summary>
public sealed record CustomDriverSetReference
{
    /// <summary>Gets the stable identifier of this profile entry (OSD generates a GUID in "N" format).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the label shown to the user, 1 to 200 characters.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Gets the immutable package that holds the driver files.</summary>
    public ContentPackageReference Package { get; init; } = new();

    /// <summary>Gets a value indicating whether the set is copied to media and offered at deployment.</summary>
    public bool IsIncluded { get; init; } = true;
}
