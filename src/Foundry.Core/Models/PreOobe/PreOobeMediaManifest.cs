// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.PreOobe;

/// <summary>Binds external package snapshots to one immutable media generation.</summary>
public sealed record PreOobeMediaManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = string.Empty;
    public IReadOnlyList<PreOobeMediaPackage> Packages { get; init; } = [];
}

/// <summary>Describes an immutable package directory relative to a verified media root.</summary>
public sealed record PreOobeMediaPackage
{
    public string ContentHash { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public PreOobePackageManifest Manifest { get; init; } = new();
}
