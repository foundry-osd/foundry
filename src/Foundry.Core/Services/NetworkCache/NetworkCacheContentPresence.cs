// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Models.Images;
using Foundry.Core.Models.Packages;
using Foundry.Core.Services.Images;
using Foundry.Core.Services.Packages;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>
/// Checks that the content a signed index references is on a share: each manifest the index binds
/// must exist with its bound hash, and every file it lists must exist with its recorded length. Only metadata is read; the
/// content hashes are verified when the deployment copies or opens the chosen files.
/// </summary>
public static class NetworkCacheContentPresence
{
    /// <summary>Item reported when the custom image manifest is missing, since the index names no image without it.</summary>
    public const string CustomImagesItem = "Custom images";

    /// <summary>Item reported when the driver set manifest is missing and the index names no set.</summary>
    public const string CustomDriverSetsItem = "Custom driver sets";

    private const int MaximumParallelChecks = 8;

    /// <summary>
    /// Returns the display names of the content that is missing or incomplete under <paramref name="contentRoot"/>: image
    /// display names, driver set names and the names of the post-installation actions whose package is affected, in index
    /// order and without duplicates. An empty list means every referenced file is present.
    /// </summary>
    /// <param name="contentRoot">The <c>&lt;share&gt;\Foundry</c> folder.</param>
    /// <param name="index">The verified index whose sections are checked.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <exception cref="IOException">The share could not be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The share refused to list or read a manifest.</exception>
    public static async Task<IReadOnlyList<string>> FindMissingAsync(string contentRoot, NetworkCacheContentIndex index, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(index);

        List<PresenceItem> items = [];
        await AddImagesAsync(contentRoot, index.CustomImages, items, cancellationToken).ConfigureAwait(false);
        await AddDriverSetsAsync(contentRoot, index.CustomDrivers, items, cancellationToken).ConfigureAwait(false);
        await AddPostInstallationPackagesAsync(contentRoot, index.PreOobe, items, cancellationToken).ConfigureAwait(false);

        // Metadata lookups over SMB are latency bound, so a few run at once; each one only marks its item.
        FileCheck[] checks = items.SelectMany((item, position) => item.Files.Select(file => new FileCheck(position, file.Path, file.Length))).ToArray();
        await Parallel.ForEachAsync(checks, new ParallelOptions { MaxDegreeOfParallelism = MaximumParallelChecks, CancellationToken = cancellationToken },
            (check, _) =>
            {
                var file = new FileInfo(check.Path);
                if (!file.Exists || file.Length != check.Length) items[check.Item].IsMissing = true;
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);

        return items.Where(item => item.IsMissing).SelectMany(item => item.Names).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static async Task AddImagesAsync(string contentRoot, DeployCustomImagesSettings settings, List<PresenceItem> items,
        CancellationToken cancellationToken)
    {
        if (!settings.IsEnabled) return;
        CustomImageMediaManifest? manifest = null;
        try
        {
            if (CustomImageMediaManifestReader.HasValidBinding(settings))
                manifest = await CustomImageMediaManifestReader.ReadAsync(contentRoot, settings, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or ArgumentException
            or FileNotFoundException or DirectoryNotFoundException)
        {
            // A damaged or replaced manifest makes the images unusable, exactly like a missing one.
        }

        if (manifest is null)
        {
            items.Add(PresenceItem.Missing([CustomImagesItem]));
            return;
        }

        foreach (CustomImageMediaEntry entry in manifest.Images)
        {
            string path = CustomImageMediaManifestReader.ResolveContainedPath(contentRoot, entry.RelativePath);
            items.Add(new([entry.Reference.DisplayName], [(path, entry.Reference.Length)]));
        }
    }

    private static async Task AddDriverSetsAsync(string contentRoot, DeployCustomDriversSettings settings, List<PresenceItem> items,
        CancellationToken cancellationToken)
    {
        // Same condition as the driver set catalog: an unbound section offers nothing, so nothing has to be present.
        if (!settings.IsEnabled || string.IsNullOrWhiteSpace(settings.ManifestId) || string.IsNullOrWhiteSpace(settings.ManifestHash)) return;
        ContentPackageMediaManifest? manifest = await ReadPackageManifestAsync(contentRoot, ContentPackageKind.Drivers,
            settings.ManifestId, settings.ManifestHash, cancellationToken).ConfigureAwait(false);
        if (manifest is null)
        {
            items.Add(PresenceItem.Missing(settings.Sets.Count == 0 ? [CustomDriverSetsItem] : settings.Sets.Select(set => set.DisplayName).ToArray()));
            return;
        }

        foreach (DeployCustomDriverSet set in settings.Sets)
            items.Add(PackageItem(contentRoot, manifest, set.ContentHash, [set.DisplayName]));
    }

    private static async Task AddPostInstallationPackagesAsync(string contentRoot, DeployPreOobeSettings settings, List<PresenceItem> items,
        CancellationToken cancellationToken)
    {
        // Only enabled actions with a package read the media generation, as in the post-installation preflight.
        IGrouping<string, PreOobeActionSettings>[] packages = settings.IsEnabled
            ? settings.Actions.Where(action => action.IsEnabled && action.Package is not null)
                .GroupBy(action => action.Package!.ContentHash, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        if (packages.Length == 0) return;
        ContentPackageMediaManifest? manifest = await ReadPackageManifestAsync(contentRoot, ContentPackageKind.PostInstallation,
            settings.ManifestId, settings.ManifestHash, cancellationToken).ConfigureAwait(false);
        foreach (IGrouping<string, PreOobeActionSettings> package in packages)
        {
            string[] names = package.Select(action => action.Name).ToArray();
            items.Add(manifest is null ? PresenceItem.Missing(names) : PackageItem(contentRoot, manifest, package.Key, names));
        }
    }

    /// <summary>Returns the manifest when it exists with the bound hash and satisfies the kind rules; otherwise <see langword="null"/>.</summary>
    private static async Task<ContentPackageMediaManifest?> ReadPackageManifestAsync(string contentRoot, ContentPackageKind kind,
        string? manifestId, string? manifestHash, CancellationToken cancellationToken)
    {
        ContentPackageMediaReadResult read = await new ContentPackageMediaReader(kind)
            .ReadAsync([contentRoot], manifestId, manifestHash, cancellationToken).ConfigureAwait(false);
        return read.Status == ContentPackageMediaStatus.Found ? read.Manifest : null;
    }

    /// <summary>Lists the files of the package <paramref name="contentHash"/>; a package the manifest does not hold exactly once is missing.</summary>
    private static PresenceItem PackageItem(string contentRoot, ContentPackageMediaManifest manifest, string contentHash, IReadOnlyList<string> names)
    {
        ContentPackageMediaPackage[] matches = manifest.Packages
            .Where(package => package.ContentHash.Equals(contentHash, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1) return PresenceItem.Missing(names);

        // The reader accepted only canonical package paths and validated file paths, so both stay under the content root.
        string packageRoot = Path.Combine(contentRoot, matches[0].RelativePath.Replace('/', Path.DirectorySeparatorChar));
        return new(names, matches[0].Manifest.Files
            .Select(file => (Path.Combine(packageRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)), file.Length))
            .ToArray());
    }

    /// <summary>One image, driver set or post-installation package, the names shown when it is missing and the files it needs.</summary>
    private sealed class PresenceItem(IReadOnlyList<string> names, IReadOnlyList<(string Path, long Length)> files)
    {
        public IReadOnlyList<string> Names { get; } = names;
        public IReadOnlyList<(string Path, long Length)> Files { get; } = files;
        public bool IsMissing { get; set; }

        public static PresenceItem Missing(IReadOnlyList<string> names) => new(names, []) { IsMissing = true };
    }

    private sealed record FileCheck(int Item, string Path, long Length);
}
