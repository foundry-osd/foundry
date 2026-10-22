// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Packages;
using Foundry.Core.Services.Packages;

namespace Foundry.Core.Services.Drivers;

/// <summary>
/// Host library of custom driver set packages. It wraps a drivers-kind <see cref="ContentPackageLibraryService"/>
/// so consumers can inject it next to the post-installation library without keyed services.
/// </summary>
public sealed class CustomDriverSetLibrary
{
    /// <summary>Creates the driver set library rooted at <paramref name="rootDirectory"/>.</summary>
    /// <param name="rootDirectory">Host directory that owns the library layout.</param>
    public CustomDriverSetLibrary(string rootDirectory)
    {
        Library = new ContentPackageLibraryService(rootDirectory, ContentPackageKind.Drivers);
    }

    /// <summary>Gets the underlying drivers-kind library, used by media preparation.</summary>
    public ContentPackageLibraryService Library { get; }

    /// <summary>Freezes a driver folder; the import is rejected when the folder holds no INF file.</summary>
    public Task<ContentPackageReference> ImportAsync(string sourcePath, CancellationToken cancellationToken = default) =>
        Library.ImportAsync(sourcePath, cancellationToken);

    /// <summary>Performs lightweight readiness checks; media preparation still acquires a fully verified lease.</summary>
    public bool IsAvailable(ContentPackageReference reference) => Library.IsAvailable(reference);

    /// <summary>Acquires a verified lease that keeps the package files in place until it is disposed.</summary>
    public Task<ContentPackageLease> AcquireAsync(string contentHash, CancellationToken cancellationToken = default) =>
        Library.AcquireAsync(contentHash, cancellationToken);

    /// <summary>Removes an unreferenced, unleased package from the library.</summary>
    public Task DeleteAsync(string contentHash, IEnumerable<string> referencedHashes, CancellationToken cancellationToken = default) =>
        Library.DeleteAsync(contentHash, referencedHashes, cancellationToken);

    /// <summary>Counts the INF files of an imported set from its manifest, without hashing the files.</summary>
    /// <returns>The INF count, or <see langword="null"/> when the package is missing or damaged.</returns>
    public int? TryCountInfFiles(string contentHash) =>
        Library.TryReadManifest(contentHash) is { } manifest ? CountInfFiles(manifest) : null;

    /// <summary>Counts the INF files of a package manifest, comparing the extension case-insensitively.</summary>
    public static int CountInfFiles(ContentPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.Files.Count(file => file.RelativePath.EndsWith(".inf", StringComparison.OrdinalIgnoreCase));
    }
}
