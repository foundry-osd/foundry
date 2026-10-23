// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>Binds the offered driver sets to a specific external media manifest, never a host library path.</summary>
public sealed record DeployCustomDriversSettings
{
    /// <summary>Gets a value indicating whether custom driver sets are offered during deployment.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>Gets the included driver sets.</summary>
    public IReadOnlyList<DeployCustomDriverSet> Sets { get; init; } = [];

    /// <summary>Gets the identifier of the preselected set, or <see langword="null"/>.</summary>
    public string? DefaultSetId { get; init; }

    /// <summary>Gets the identifier of the media manifest written during media preparation.</summary>
    public string? ManifestId { get; init; }

    /// <summary>Gets the hash of the media manifest written during media preparation.</summary>
    public string? ManifestHash { get; init; }
}

/// <summary>Describes one driver set offered at deployment; the content hash is the identity used to find its files on the media.</summary>
public sealed record DeployCustomDriverSet
{
    /// <summary>Gets the stable identifier of the configuration entry.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the label shown in the deployment wizard.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Gets the SHA-256 of the package manifest.</summary>
    public string ContentHash { get; init; } = string.Empty;

    /// <summary>Gets the total length of the package files in bytes.</summary>
    public long Length { get; init; }

    /// <summary>Gets the number of files in the package.</summary>
    public int FileCount { get; init; }
}
