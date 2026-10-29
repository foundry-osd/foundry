// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Images;
using Foundry.Core.Models.Packages;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Images;
using Foundry.Core.Services.Packages;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>Space used on one share, in bytes.</summary>
/// <param name="DownloadsBytes">Files under <c>Foundry\Downloads</c>, published by devices.</param>
/// <param name="ImportedContentBytes">Files under <c>Foundry\Cache</c>, the imported content published by Foundry OSD.</param>
public sealed record NetworkCacheShareUsage(long DownloadsBytes, long ImportedContentBytes);

/// <summary>Outcome of cleaning up one share.</summary>
/// <param name="RemovedImages">Custom image folders removed.</param>
/// <param name="RemovedPackages">Post-installation package and custom driver set folders removed.</param>
/// <param name="RemovedManifests">Media manifests removed because none of the content they describe remains.</param>
/// <param name="RetainedEntries">
/// Share paths, relative to <c>Foundry</c> with forward slashes, left in place because they are in use, redirected, changed
/// since publication, or not described by any manifest.
/// </param>
public sealed record NetworkCacheCleanUpResult(int RemovedImages, int RemovedPackages, int RemovedManifests, IReadOnlyList<string> RetainedEntries);

/// <summary>
/// Measures and cleans up the Foundry folder of a network cache share. Clean up deletes every download and the imported
/// content that neither the current configuration nor any signed index on the share references; content they reference is
/// kept, as are the manifests that still describe kept content. Only what a manifest proves Foundry wrote is deleted, never through a reparse point.
/// Every method blocks on file system calls and runs on the thread pool.
/// </summary>
public static class NetworkCacheShareMaintenance
{
    /// <summary>Folder under <c>Foundry</c> that holds the imported content, laid out as on media.</summary>
    public const string ImportedContentFolderName = "Cache";

    private const int MaximumImageManifestBytes = CustomImageMediaManifestReader.MaximumManifestBytes;
    // A writer may still be using an entry written within this period: an index temporary file, or content whose index is not written yet.
    private static readonly TimeSpan PublicationGracePeriod = TimeSpan.FromHours(24);
    private const string ImageFileName = "image.wim";
    private const string ManifestsFolderName = "manifests";
    // Hidden and system entries are listed too: a skipped entry could hide a reparse point or keep a folder alive.
    private static readonly EnumerationOptions AllEntries = new() { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false };
    private static readonly EnumerationOptions MeasuredFiles = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true,
        RecurseSubdirectories = true
    };

    /// <summary>Returns the size of the downloads and of the imported content under <paramref name="contentRoot"/>.</summary>
    /// <param name="contentRoot">The <c>&lt;share&gt;\Foundry</c> folder; a missing folder measures zero.</param>
    /// <param name="cancellationToken">Cancels the measurement.</param>
    public static Task<NetworkCacheShareUsage> MeasureAsync(string contentRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        return Task.Run(() => new NetworkCacheShareUsage(
            Measure(Path.Combine(contentRoot, NetworkCacheLayout.DownloadsFolderName), cancellationToken),
            Measure(Path.Combine(contentRoot, ImportedContentFolderName), cancellationToken)), cancellationToken);
    }

    /// <summary>
    /// Deletes <c>Foundry\Downloads</c> and the imported content that neither <paramref name="configuration"/> nor any signed
    /// index on the share references. The index files themselves stay, since another Foundry OSD publishes through its own
    /// key; only their temporary files older than a day are deleted. Media built from an older configuration then loses the
    /// imported content it referenced on this share.
    /// </summary>
    /// <remarks>
    /// An index binds media manifests, not content hashes: the content kept for it is what the manifests it names describe.
    /// Indexes are read without checking signatures and manifests without checking their hash, since keeping more is always safe.
    /// The downloads go first; the indexes and the manifests they bind are then read while the publication lock of every
    /// imported content kind is held, so an index or manifest that cannot be read stops Clean up before any imported content
    /// is deleted. Content folders and manifests written within the last 24 hours stay, with the content those manifests
    /// describe, because another Foundry OSD writes its index only after it has published the content.
    /// </remarks>
    /// <param name="contentRoot">The <c>&lt;share&gt;\Foundry</c> folder.</param>
    /// <param name="configuration">The configuration whose content references are kept, included or not.</param>
    /// <param name="cancellationToken">Cancels between entries; what was deleted stays deleted.</param>
    /// <exception cref="IOException">Another Foundry OSD is publishing imported content to the share.</exception>
    /// <exception cref="InvalidDataException">The <c>Foundry</c> folder is a reparse point; nothing is deleted.</exception>
    public static Task<NetworkCacheCleanUpResult> CleanUpAsync(string contentRoot, FoundryConfigurationDocument configuration, CancellationToken cancellationToken) =>
        CleanUpAsync(contentRoot, configuration, TimeProvider.System, afterDownloadsDeleted: null, cancellationToken);

    /// <summary>Cleans up with an explicit clock and a step that runs once the downloads are deleted, before any content is locked.</summary>
    internal static Task<NetworkCacheCleanUpResult> CleanUpAsync(string contentRoot, FoundryConfigurationDocument configuration, TimeProvider timeProvider,
        Func<CancellationToken, Task>? afterDownloadsDeleted, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(timeProvider);
        return Task.Run(() => CleanUpOnPoolAsync(contentRoot, configuration, timeProvider, afterDownloadsDeleted, cancellationToken), cancellationToken);
    }

    private static async Task<NetworkCacheCleanUpResult> CleanUpOnPoolAsync(string contentRoot, FoundryConfigurationDocument configuration,
        TimeProvider timeProvider, Func<CancellationToken, Task>? afterDownloadsDeleted, CancellationToken cancellationToken)
    {
        // Every rule below proves entries inside the share; a redirected Foundry folder would apply them somewhere else.
        var root = new DirectoryInfo(contentRoot);
        if (root.Exists && IsReparsePoint(root)) throw new InvalidDataException("The Foundry folder of the share is a redirection.");
        DateTime threshold = timeProvider.GetUtcNow().UtcDateTime - PublicationGracePeriod;
        List<string> retained = [];
        string downloads = Path.Combine(contentRoot, NetworkCacheLayout.DownloadsFolderName);
        if (Directory.Exists(downloads)) DeleteTree(contentRoot, new DirectoryInfo(downloads), retained, cancellationToken);
        DeleteStaleIndexTemporaryFiles(contentRoot, threshold, retained, cancellationToken);
        if (afterDownloadsDeleted is not null) await afterDownloadsDeleted(cancellationToken).ConfigureAwait(false);

        // Publication holds these locks while it stages content and exposes its manifest, then writes its index. Holding all
        // of them before reading the indexes keeps content published meanwhile out of the prune, or makes it recent enough
        // to be kept until its index is written.
        var driverService = new WinPeContentPackageMediaService(ContentPackageKind.Drivers);
        var packageService = new WinPeContentPackageMediaService(ContentPackageKind.PostInstallation);
        string imagesRoot = CustomImagePathPolicy.ResolveRelativePath(contentRoot, CustomImageMediaPaths.RelativeRoot);
        using FileStream? imagesLock = OpenImagePublicationLock(contentRoot, imagesRoot, retained);
        using FileStream? driversLock = driverService.OpenPublicationLock(contentRoot);
        using FileStream? packagesLock = packageService.OpenPublicationLock(contentRoot);
        // An index or manifest that cannot be read stops Clean up before any imported content is deleted.
        IndexedContent indexed = await ReadIndexedContentAsync(contentRoot, cancellationToken).ConfigureAwait(false);
        (int removedImages, int removedImageManifests) = imagesLock is null ? (0, 0) : await PruneImagesAsync(contentRoot, imagesRoot,
            Union(ConfigurationContentReferences.CustomImageHashes(configuration), indexed.Images), threshold, retained, cancellationToken).ConfigureAwait(false);
        // A kind without its lock has no media root yet; a first publication of that kind may be staging it, so it is left alone.
        ContentPackagePruneResult drivers = driversLock is null ? NothingPruned : await driverService.PruneUnreferencedAsync(contentRoot,
            Union(ConfigurationContentReferences.DriverSetHashes(configuration), indexed.Drivers), threshold, cancellationToken).ConfigureAwait(false);
        ContentPackagePruneResult packages = packagesLock is null ? NothingPruned : await packageService.PruneUnreferencedAsync(contentRoot,
            Union(ConfigurationContentReferences.PostInstallationPackageHashes(configuration), indexed.Packages), threshold, cancellationToken).ConfigureAwait(false);
        retained.AddRange(drivers.RetainedUnknownEntries);
        retained.AddRange(packages.RetainedUnknownEntries);
        return new NetworkCacheCleanUpResult(
            removedImages,
            drivers.RemovedPackages + packages.RemovedPackages,
            removedImageManifests + drivers.RemovedManifests + packages.RemovedManifests,
            retained);
    }

    private static readonly ContentPackagePruneResult NothingPruned = new(0, 0, []);

    /// <summary>Content hashes described by the manifests the indexes on the share bind, per kind.</summary>
    private sealed record IndexedContent(HashSet<string> Images, HashSet<string> Drivers, HashSet<string> Packages);

    private static HashSet<string> Union(IReadOnlySet<string> referenced, HashSet<string> indexed) =>
        new(referenced.Concat(indexed), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Collects the content hashes of the image, custom driver and post-installation manifests that every index binds. A
    /// manifest that is missing, oversized or redirected adds nothing: it protects no content anyone could use. A manifest that exists but cannot be read throws, so Clean up stops before it deletes content that manifest may protect.
    /// </summary>
    private static async Task<IndexedContent> ReadIndexedContentAsync(string contentRoot, CancellationToken cancellationToken)
    {
        var content = new IndexedContent(new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase));
        foreach (NetworkCacheContentIndex index in await NetworkCacheIndexStore.ReadAllUnverifiedAsync(contentRoot, cancellationToken).ConfigureAwait(false))
        {
            await AddImageHashesAsync(contentRoot, index.CustomImages.ManifestId, content.Images, cancellationToken).ConfigureAwait(false);
            await AddPackageHashesAsync(contentRoot, ContentPackageKind.Drivers, index.CustomDrivers.ManifestId, content.Drivers, cancellationToken).ConfigureAwait(false);
            await AddPackageHashesAsync(contentRoot, ContentPackageKind.PostInstallation, index.PreOobe.ManifestId, content.Packages, cancellationToken).ConfigureAwait(false);
        }

        return content;
    }

    private static async Task AddImageHashesAsync(string contentRoot, string? manifestId, HashSet<string> hashes, CancellationToken cancellationToken)
    {
        // The id becomes a file name, so only a manifest id is used.
        if (ManifestFileId(manifestId) is not { } id) return;
        string imagesRoot = CustomImagePathPolicy.ResolveRelativePath(contentRoot, CustomImageMediaPaths.RelativeRoot);
        byte[]? bytes = await ReadManifestAsync(Path.Combine(imagesRoot, ManifestsFolderName, id + ".json"), MaximumImageManifestBytes, cancellationToken).ConfigureAwait(false);
        CustomImageMediaManifest? manifest = bytes is null ? null : TryParseImageManifest(bytes, id);
        if (manifest is null) return;
        foreach (CustomImageMediaEntry entry in manifest.Images)
        {
            string hash = entry.Reference.ContentHash.ToLowerInvariant();
            if (IsHash(hash)) hashes.Add(hash);
        }
    }

    private static async Task AddPackageHashesAsync(string contentRoot, ContentPackageKind kind, string? manifestId, HashSet<string> hashes,
        CancellationToken cancellationToken)
    {
        if (ManifestFileId(manifestId) is not { } id) return;
        string path = Path.Combine(contentRoot, kind.MediaRoot.Replace('/', Path.DirectorySeparatorChar), ManifestsFolderName, id + ".json");
        byte[]? bytes = await ReadManifestAsync(path, kind.MaximumMediaManifestBytes, cancellationToken).ConfigureAwait(false);
        ContentPackageMediaManifest? manifest = bytes is null ? null : new ContentPackageMediaReader(kind).Parse(bytes, id);
        if (manifest is null) return;
        foreach (ContentPackageMediaPackage package in manifest.Packages) hashes.Add(package.ContentHash);
    }

    private static async Task<byte[]?> ReadManifestAsync(string path, long maximumBytes, CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists || IsReparsePoint(file) || file.Length > maximumBytes) return null;
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return bytes.Length <= maximumBytes ? bytes : null;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Deletes the temporary files an interrupted index write left, once a writer cannot still be using them.</summary>
    private static void DeleteStaleIndexTemporaryFiles(string contentRoot, DateTime threshold, List<string> retained, CancellationToken cancellationToken)
    {
        var folder = new DirectoryInfo(Path.Combine(contentRoot, NetworkCacheIndexStore.FolderName));
        if (!folder.Exists || IsReparsePoint(folder)) return;
        foreach (FileSystemInfo entry in Entries(folder.FullName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry is FileInfo file && !IsReparsePoint(file)
                && file.Name.EndsWith(NetworkCacheIndexStore.TemporarySuffix, StringComparison.OrdinalIgnoreCase)
                && file.LastWriteTimeUtc < threshold && !TryDelete(file))
                Retain(contentRoot, file.FullName, retained);
        }
    }

    private static long Measure(string directory, CancellationToken cancellationToken)
    {
        var root = new DirectoryInfo(directory);
        if (!root.Exists || IsReparsePoint(root)) return 0;
        long total = 0;
        foreach (FileInfo file in root.EnumerateFiles("*", MeasuredFiles))
        {
            cancellationToken.ThrowIfCancellationRequested();
            total = checked(total + file.Length);
        }

        return total;
    }

    /// <summary>
    /// Deletes a tree bottom-up, entry by entry, so a file a device still writes or reads only keeps itself and its folders.
    /// A reparse point is kept and never followed.
    /// </summary>
    private static void DeleteTree(string contentRoot, DirectoryInfo directory, List<string> retained, CancellationToken cancellationToken)
    {
        if (IsReparsePoint(directory))
        {
            Retain(contentRoot, directory.FullName, retained);
            return;
        }

        int retainedBefore = retained.Count;
        foreach (FileSystemInfo entry in Entries(directory.FullName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry is DirectoryInfo child) DeleteTree(contentRoot, child, retained, cancellationToken);
            else if (IsReparsePoint(entry) || !TryDelete(entry)) Retain(contentRoot, entry.FullName, retained);
        }

        // A folder that kept an entry is already explained by it.
        if (!TryDelete(directory) && retained.Count == retainedBefore) Retain(contentRoot, directory.FullName, retained);
    }

    /// <summary>
    /// Takes the publication lock that media publication holds on the custom image folder, or returns <see langword="null"/>
    /// when there is no folder to prune; a redirected folder is reported and never entered.
    /// </summary>
    private static FileStream? OpenImagePublicationLock(string contentRoot, string imagesRoot, List<string> retained)
    {
        var rootInfo = new DirectoryInfo(imagesRoot);
        if (!rootInfo.Exists) return null;
        if (IsReparsePoint(rootInfo))
        {
            Retain(contentRoot, imagesRoot, retained);
            return null;
        }

        return new FileStream(Path.Combine(imagesRoot, WinPeMediaFileTransfer.PublicationLockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>
    /// Removes custom image folders that no manifest-described, unreferenced hash protects, then the manifests none of whose
    /// images remains. The caller holds the image publication lock. Image folders and manifests written since
    /// <paramref name="keepWrittenSinceUtc"/> stay, with every image those manifests describe.
    /// </summary>
    private static async Task<(int RemovedImages, int RemovedManifests)> PruneImagesAsync(string contentRoot, string imagesRoot, IReadOnlySet<string> referenced,
        DateTime keepWrittenSinceUtc, List<string> retained, CancellationToken cancellationToken)
    {
        Dictionary<string, CustomImageMediaManifest> manifests = await ReadImageManifestsAsync(contentRoot, imagesRoot, retained, cancellationToken).ConfigureAwait(false);
        HashSet<string> kept = new(referenced, StringComparer.OrdinalIgnoreCase);
        foreach (string name in manifests.Keys.ToList())
        {
            if (File.GetLastWriteTimeUtc(Path.Combine(imagesRoot, ManifestsFolderName, name)) < keepWrittenSinceUtc) continue;
            kept.UnionWith(manifests[name].Images.Select(image => image.Reference.ContentHash.ToLowerInvariant()));
            manifests.Remove(name);
        }

        Dictionary<string, long> described = new(StringComparer.OrdinalIgnoreCase);
        foreach (CustomImageMediaEntry entry in manifests.Values.SelectMany(manifest => manifest.Images))
        {
            string hash = entry.Reference.ContentHash.ToLowerInvariant();
            if (IsHash(hash) && IsImagePath(entry.RelativePath, hash)) described.TryAdd(hash, entry.Reference.Length);
        }

        int removedImages = 0;
        foreach (FileSystemInfo entry in Entries(imagesRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Publication stages under the lock held here, so every staging folder is left by an interrupted publication.
            if (entry is DirectoryInfo staging && staging.Name.StartsWith(".pending-", StringComparison.Ordinal))
            {
                DeleteTree(contentRoot, staging, retained, cancellationToken);
                continue;
            }

            // Other dot entries are the publication lock; the manifests are handled below.
            if (entry.Name.StartsWith('.') || entry.Name.Equals(ManifestsFolderName, StringComparison.OrdinalIgnoreCase)) continue;
            if (entry is DirectoryInfo && (kept.Contains(entry.Name) || entry.LastWriteTimeUtc >= keepWrittenSinceUtc)) continue;
            if (entry is not DirectoryInfo folder || IsReparsePoint(folder) || !described.TryGetValue(folder.Name, out long length))
                Retain(contentRoot, entry.FullName, retained);
            else if (RemoveImage(contentRoot, folder, length, retained)) removedImages++;
        }

        int removedManifests = 0;
        foreach ((string name, CustomImageMediaManifest manifest) in manifests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (manifest.Images.Count == 0 ||
                manifest.Images.Any(image => Directory.Exists(Path.Combine(imagesRoot, image.Reference.ContentHash.ToLowerInvariant()))))
                continue;
            var file = new FileInfo(Path.Combine(imagesRoot, ManifestsFolderName, name));
            if (file.Exists && !IsReparsePoint(file) && TryDelete(file)) removedManifests++;
        }

        return (removedImages, removedManifests);
    }

    private static async Task<Dictionary<string, CustomImageMediaManifest>> ReadImageManifestsAsync(string contentRoot, string imagesRoot,
        List<string> retained, CancellationToken cancellationToken)
    {
        Dictionary<string, CustomImageMediaManifest> manifests = new(StringComparer.OrdinalIgnoreCase);
        var directory = new DirectoryInfo(Path.Combine(imagesRoot, ManifestsFolderName));
        if (!directory.Exists) return manifests;
        if (IsReparsePoint(directory))
        {
            Retain(contentRoot, directory.FullName, retained);
            return manifests;
        }

        foreach (FileSystemInfo entry in Entries(directory.FullName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CustomImageMediaManifest? manifest = null;
            if (entry is FileInfo file && !IsReparsePoint(file) && IsManifestName(file.Name) && file.Length <= MaximumImageManifestBytes)
            {
                byte[] bytes = await File.ReadAllBytesAsync(file.FullName, cancellationToken).ConfigureAwait(false);
                manifest = TryParseImageManifest(bytes, file.Name[..32]);
            }

            if (manifest is null) Retain(contentRoot, entry.FullName, retained);
            else manifests[entry.Name] = manifest;
        }

        return manifests;
    }

    private static CustomImageMediaManifest? TryParseImageManifest(byte[] bytes, string expectedId)
    {
        try
        {
            CustomImageMediaManifest? manifest = JsonSerializer.Deserialize<CustomImageMediaManifest>(bytes, ConfigurationJsonDefaults.SerializerOptions);
            return manifest is not null && manifest.Images is not null && string.Equals(manifest.ManifestId, expectedId, StringComparison.Ordinal) ? manifest : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Deletes the image when its length still matches its description, then the folder when it is empty.</summary>
    private static bool RemoveImage(string contentRoot, DirectoryInfo folder, long length, List<string> retained)
    {
        var image = new FileInfo(Path.Combine(folder.FullName, ImageFileName));
        if (image.Exists && !IsReparsePoint(image) && image.Length == length) TryDelete(image);
        List<FileSystemInfo> leftovers = Entries(folder.FullName);
        if (leftovers.Count == 0 && TryDelete(folder)) return true;
        foreach (FileSystemInfo leftover in leftovers) Retain(contentRoot, leftover.FullName, retained);
        if (leftovers.Count == 0) Retain(contentRoot, folder.FullName, retained);
        return false;
    }

    private static bool IsImagePath(string relativePath, string hash) =>
        string.Equals(relativePath.Replace('\\', '/'), $"{CustomImageMediaPaths.RelativeRoot}/{hash}/{ImageFileName}", StringComparison.OrdinalIgnoreCase);

    private static bool IsHash(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigit);

    /// <summary>Returns a media manifest id (32 hexadecimal characters) in the lower case of its file name, or <see langword="null"/>.</summary>
    private static string? ManifestFileId(string? manifestId) =>
        manifestId is { Length: 32 } && manifestId.All(char.IsAsciiHexDigit) ? manifestId.ToLowerInvariant() : null;

    private static bool IsManifestName(string name) =>
        name.Length == 37 && name.EndsWith(".json", StringComparison.Ordinal) && name[..32].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static List<FileSystemInfo> Entries(string directory) => new DirectoryInfo(directory).EnumerateFileSystemInfos("*", AllEntries).ToList();

    private static bool IsReparsePoint(FileSystemInfo entry) => (entry.Attributes & FileAttributes.ReparsePoint) != 0;

    private static bool TryDelete(FileSystemInfo entry)
    {
        try
        {
            if (entry is FileInfo { IsReadOnly: true } file) file.IsReadOnly = false;
            entry.Delete();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Retain(string contentRoot, string path, List<string> retained) =>
        retained.Add(Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/'));
}
