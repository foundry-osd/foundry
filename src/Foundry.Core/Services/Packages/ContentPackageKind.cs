// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Packages;

/// <summary>
/// Describes the limits and layout rules of one kind of immutable content package.
/// Every kind shares the manifest format, the content hash and the library layout; only these values differ.
/// </summary>
public sealed record ContentPackageKind
{
    /// <summary>Stable kind name used in diagnostics.</summary>
    public required string Name { get; init; }

    /// <summary>Maximum length of a package-relative path, using forward slashes.</summary>
    public required int MaximumRelativePathLength { get; init; }

    /// <summary>Maximum length of a resolved absolute path, including the host or target prefix.</summary>
    public required int MaximumFullPathLength { get; init; }

    /// <summary>Maximum number of files in one package.</summary>
    public required int MaximumFiles { get; init; }

    /// <summary>Maximum number of directories in one package.</summary>
    public required int MaximumDirectories { get; init; }

    /// <summary>Maximum size of the media manifest that binds package snapshots to one media generation.</summary>
    public required int MaximumMediaManifestBytes { get; init; }

    /// <summary>Media-relative root that holds this kind's package snapshots, using forward slashes.</summary>
    public required string MediaRoot { get; init; }

    /// <summary>Whether a package of this kind must contain at least one INF file.</summary>
    public required bool RequiresInfFile { get; init; }

    /// <summary>Whether updating USB media removes packages that are no longer referenced.</summary>
    public required bool PrunesMediaOnUpdate { get; init; }

    /// <summary>Packages that run before OOBE; paths stay short enough for legacy installer tools on the target.</summary>
    public static ContentPackageKind PostInstallation { get; } = new()
    {
        Name = "PostInstallation",
        MaximumRelativePathLength = 110,
        MaximumFullPathLength = 259,
        MaximumFiles = 10_000,
        MaximumDirectories = 10_000,
        MaximumMediaManifestBytes = 16 * 1024 * 1024,
        MediaRoot = "Cache/PreOobe",
        RequiresInfFile = false,
        PrunesMediaOnUpdate = false
    };

    /// <summary>Custom driver sets; paths keep the full host and USB prefix within the long-path limit of Windows.</summary>
    public static ContentPackageKind Drivers { get; } = new()
    {
        Name = "Drivers",
        MaximumRelativePathLength = 150,
        MaximumFullPathLength = 32_767,
        MaximumFiles = 20_000,
        MaximumDirectories = 20_000,
        MaximumMediaManifestBytes = 64 * 1024 * 1024,
        MediaRoot = "Cache/DriverPacks/Custom",
        RequiresInfFile = true,
        PrunesMediaOnUpdate = true
    };
}
