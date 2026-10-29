// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Packages;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Images;
using Foundry.Core.Services.Packages;
using Foundry.Utilities.Storage;
using Serilog;

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Publishes complete content package generations of one kind outside boot.wim; only kinds that prune on USB update remove older generations, through the prune methods.
/// Temporary media staging supports long host paths; installer path limits apply separately to package imports and target staging.
/// </summary>
public sealed class WinPeContentPackageMediaService : IWinPeContentPackageMediaPublisher
{
    internal const long ReserveBytes = 64L * 1024 * 1024;
    private static readonly JsonSerializerOptions MediaJsonOptions = new(ConfigurationJsonDefaults.SerializerOptions) { WriteIndented = false };
    // Hidden and system entries are listed too: a skipped entry could hide a reparse point or keep a folder alive.
    private static readonly EnumerationOptions AllEntries = new() { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false };
    private readonly ContentPackageKind kind;
    private readonly Func<string, long> availableBytes;
    private readonly string stagingStatus;
    private readonly string verifiedStatus;

    /// <summary>Creates a publisher for one content package kind.</summary>
    /// <param name="kind">The content package kind that drives the media root and the media manifest limit.</param>
    public WinPeContentPackageMediaService(ContentPackageKind kind) : this(kind, WindowsVolumeStorage.GetAvailableBytes) { }

    internal WinPeContentPackageMediaService(ContentPackageKind kind, Func<string, long> availableBytes)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(availableBytes);
        this.kind = kind;
        this.availableBytes = availableBytes;
        // Statuses follow the kind so the progress text names the content being copied.
        (stagingStatus, verifiedStatus) = kind == ContentPackageKind.Drivers
            ? (WinPeProgressStatuses.StagingCustomDrivers, WinPeProgressStatuses.CustomDriversVerified)
            : (WinPeProgressStatuses.StagingPostInstallation, WinPeProgressStatuses.PostInstallationVerified);
    }

    /// <summary>Freezes the referenced package versions before boot configuration is generated.</summary>
    public async Task<WinPeContentPackageMediaLease> PrepareAsync(ContentPackageLibraryService library, IReadOnlyList<ContentPackageReference> references,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(references);
        if (library.Kind != kind) throw new ArgumentException($"The library holds '{library.Kind.Name}' packages but this service publishes '{kind.Name}' packages.", nameof(library));
        List<IDisposable> leases = [];
        try
        {
            List<ContentPackageMediaPackage> packages = [];
            List<WinPeContentPackageMediaFile> files = [];
            List<string> directories = [];
            long manifestBudget = 4096;
            foreach (ContentPackageReference reference in references.DistinctBy(package => package.ContentHash, StringComparer.OrdinalIgnoreCase))
            {
                ContentPackageLease lease = await library.AcquireAsync(reference.ContentHash, cancellationToken).ConfigureAwait(false);
                leases.Add(lease);
                if (lease.Reference.Length != reference.Length || lease.Reference.FileCount != reference.FileCount)
                    throw new InvalidDataException("ContentPackage.InvalidPackageReference");
                string prefix = $"{kind.MediaRoot}/Packages/{reference.ContentHash.ToLowerInvariant()}/files";
                var mediaPackage = new ContentPackageMediaPackage { ContentHash = reference.ContentHash.ToLowerInvariant(), RelativePath = prefix, Manifest = lease.Manifest };
                manifestBudget = checked(manifestBudget + JsonSerializer.SerializeToUtf8Bytes(mediaPackage, MediaJsonOptions).Length);
                if (manifestBudget > kind.MaximumMediaManifestBytes) throw new InvalidDataException("ContentPackage.MediaManifestTooLarge");
                packages.Add(mediaPackage);
                directories.Add(prefix);
                directories.AddRange(lease.Manifest.Directories.Select(directory => $"{prefix}/{directory}"));
                files.AddRange(lease.Files.Select(file => new WinPeContentPackageMediaFile(file.SourcePath, $"{prefix}/{file.RelativePath}", file.Length, file.Sha256)));
            }
            // The id comes from the manifest content, so the same packages always give the same manifest bytes and id.
            var unnamed = new ContentPackageMediaManifest { Packages = packages };
            var manifest = unnamed with { Id = MediaManifestId.From(JsonSerializer.SerializeToUtf8Bytes(unnamed, MediaJsonOptions)) };
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, MediaJsonOptions);
            if (bytes.Length > kind.MaximumMediaManifestBytes) throw new InvalidDataException("ContentPackage.MediaManifestTooLarge");
            return new(kind, manifest, bytes, files, directories, leases);
        }
        catch { foreach (IDisposable lease in leases.AsEnumerable().Reverse()) lease.Dispose(); throw; }
    }

    /// <summary>Verifies that every held source file still matches its recorded length and SHA-256.</summary>
    public async Task ValidateSourcesAsync(WinPeContentPackageMediaLease package, CancellationToken cancellationToken)
    {
        ThrowIfInvalidLease(package);
        foreach (WinPeContentPackageMediaFile file in package.Files)
            if (!await MatchesAsync(file.SourcePath, file.Length, file.ContentHash, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("ContentPackage.PackageContentChanged");
    }

    /// <summary>Counts only new/replacement bytes while retaining prior files, generations and a staging reserve.</summary>
    public async Task<long> GetRequiredBytesAsync(WinPeContentPackageMediaLease package, string root, CancellationToken cancellationToken)
    {
        ThrowIfInvalidLease(package);
        long required = checked(ReserveBytes + package.ManifestBytes.LongLength);
        foreach (WinPeContentPackageMediaFile file in package.Files)
            if (!await MatchesAsync(CustomImagePathPolicy.ResolveRelativePath(root, file.RelativePath), file.Length, file.ContentHash, cancellationToken).ConfigureAwait(false))
                required = checked(required + file.Length);
        return required;
    }

    /// <summary>Throws when the media volume cannot hold the required bytes plus <paramref name="additionalBytes"/>.</summary>
    public async Task ValidateCapacityAsync(WinPeContentPackageMediaLease package, string root, long additionalBytes, CancellationToken cancellationToken)
    {
        ThrowIfInvalidLease(package);
        if (availableBytes(root) < checked(await GetRequiredBytesAsync(package, root, cancellationToken).ConfigureAwait(false) + additionalBytes))
            throw new WinPeMediaCapacityException("ContentPackage.InsufficientMediaSpace");
    }

    /// <summary>Verifies staged files before exposing the manifest, so canceled publication leaves older boot configurations usable.</summary>
    public Task PublishAsync(WinPeContentPackageMediaLease package, string root, CancellationToken cancellationToken,
        IProgress<WinPeMediaProgress>? progress = null) =>
        PublishCoreAsync(package, root, isNetworkCacheShare: false, cancellationToken, progress);

    /// <summary>
    /// Publishes to a network cache share without reading the share back: a file already at its content path with the recorded
    /// length counts as present, and a copy is checked against the bytes read from the source. Foundry Deploy verifies the hash
    /// of every file it uses.
    /// </summary>
    public Task PublishToNetworkCacheAsync(WinPeContentPackageMediaLease package, string contentRoot, CancellationToken cancellationToken,
        IProgress<WinPeMediaProgress>? progress = null) =>
        PublishCoreAsync(package, contentRoot, isNetworkCacheShare: true, cancellationToken, progress);

    private async Task PublishCoreAsync(WinPeContentPackageMediaLease package, string root, bool isNetworkCacheShare,
        CancellationToken cancellationToken, IProgress<WinPeMediaProgress>? progress)
    {
        ThrowIfInvalidLease(package);
        // On a share each copied file is checked against the bytes read from its source, and present files never read it.
        if (!isNetworkCacheShare) await ValidateSourcesAsync(package, cancellationToken).ConfigureAwait(false);
        string ownedRoot = CustomImagePathPolicy.ResolveRelativePath(root, kind.MediaRoot);
        Directory.CreateDirectory(ownedRoot);
        using var publicationLock = new FileStream(CustomImagePathPolicy.ResolveRelativePath(ownedRoot, WinPeMediaFileTransfer.PublicationLockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var byteProgress = WinPeMediaByteProgress.ForFiles(progress, package.Files.Select(file => file.Length), stagingStatus);
        // Each destination is read at most once: what already matches is reused, and only the rest counts against free space.
        List<(WinPeContentPackageMediaFile File, string Destination)> missing = [];
        long required = checked(ReserveBytes + package.ManifestBytes.LongLength);
        foreach (WinPeContentPackageMediaFile file in package.Files)
        {
            string destination = CustomImagePathPolicy.ResolveRelativePath(root, file.RelativePath);
            bool matches = isNetworkCacheShare
                ? WinPeMediaFileTransfer.HasLength(destination, file.Length, byteProgress.Full)
                : await MatchesAsync(destination, file.Length, file.ContentHash, cancellationToken, byteProgress.Full).ConfigureAwait(false);
            byteProgress.EndOfFile();
            if (matches) continue;
            missing.Add((file, destination));
            required = checked(required + file.Length);
        }
        if (availableBytes(root) < required) throw new WinPeMediaCapacityException("ContentPackage.InsufficientMediaSpace");
        if (isNetworkCacheShare)
        {
            // Reused packages and manifests keep their old write time; refreshing it under the lock keeps Clean up on another PC
            // from removing them before this publication writes the index that binds them.
            foreach (ContentPackageMediaPackage mediaPackage in package.Manifest.Packages)
                WinPeMediaFileTransfer.MarkInUse(CustomImagePathPolicy.ResolveRelativePath(root, mediaPackage.RelativePath));
            WinPeMediaFileTransfer.MarkInUse(CustomImagePathPolicy.ResolveRelativePath(root, package.ManifestRelativePath));
        }
        string pending = CustomImagePathPolicy.ResolveRelativePath(ownedRoot, ".pending-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pending);
        try
        {
            List<(string Temporary, string Destination)> copied = [];
            foreach ((WinPeContentPackageMediaFile file, string destination) in missing)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string temporary = Path.Combine(pending, Guid.NewGuid().ToString("N"));
                // On a share the copy is checked against the bytes read from the source; elsewhere the copy is read back.
                bool verified = isNetworkCacheShare
                    ? (await WinPeMediaFileTransfer.CopyAsync(file.SourcePath, temporary, byteProgress.Full, cancellationToken).ConfigureAwait(false))
                        .Matches(file.Length, file.ContentHash)
                    : await CopyAndReadBackAsync(file, temporary, byteProgress, cancellationToken).ConfigureAwait(false);
                if (!verified) throw new InvalidDataException("ContentPackage.PackageContentChanged");
                byteProgress.EndOfFile();
                copied.Add((temporary, destination));
            }
            foreach (string directory in package.Directories) Directory.CreateDirectory(CustomImagePathPolicy.ResolveRelativePath(root, directory));
            foreach ((string temporary, string destination) in copied)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CustomImagePathPolicy.ValidateNoReparsePoints(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(temporary, destination, overwrite: true);
            }
            string manifest = CustomImagePathPolicy.ResolveRelativePath(root, package.ManifestRelativePath);
            if (File.Exists(manifest))
            {
                if (!await MatchesAsync(manifest, package.ManifestBytes.Length, package.ManifestHash, cancellationToken).ConfigureAwait(false))
                    throw new InvalidDataException("ContentPackage.InvalidMediaBinding");
            }
            else
            {
                string temporary = Path.Combine(pending, "manifest.json");
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await output.WriteAsync(package.ManifestBytes, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    output.Flush(flushToDisk: true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
                File.Move(temporary, manifest);
            }
            byteProgress.Complete(100, verifiedStatus);
        }
        finally
        {
            CustomImagePathPolicy.ValidateNoReparsePoints(pending);
            foreach (string file in Directory.EnumerateFiles(pending))
            {
                CustomImagePathPolicy.ValidateNoReparsePoints(file);
                File.Delete(file);
            }
            Directory.Delete(pending, recursive: false);
        }
    }

    /// <summary>
    /// Removes package folders and media manifests that <paramref name="current"/> no longer references, for kinds that prune on USB update.
    /// </summary>
    /// <remarks>
    /// Runs under the publication lock after <paramref name="current"/> was published. Only files and folders that a previous
    /// valid manifest describes are deleted, each file after a recorded-length check and folders bottom-up. A manifest is
    /// deleted only after every folder it describes is gone or still referenced, so a failed run keeps a description for
    /// anything it left behind. Unknown entries and anything reached through a reparse point stay in place and are reported.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The kind does not prune media on update.</exception>
    public async Task<ContentPackagePruneResult> PruneAsync(WinPeContentPackageMediaLease current, string root, CancellationToken cancellationToken)
    {
        if (!kind.PrunesMediaOnUpdate) throw new InvalidOperationException($"'{kind.Name}' media content is not pruned on update.");
        ThrowIfInvalidLease(current);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string ownedRoot = CustomImagePathPolicy.ResolveRelativePath(root, kind.MediaRoot);
        if (!Directory.Exists(ownedRoot)) throw new InvalidDataException("ContentPackage.InvalidMediaBinding");
        using var publicationLock = new FileStream(CustomImagePathPolicy.ResolveRelativePath(ownedRoot, WinPeMediaFileTransfer.PublicationLockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        // Never prune against a generation that is not on the media: its references are the only protection of live content.
        if (!await MatchesAsync(CustomImagePathPolicy.ResolveRelativePath(root, current.ManifestRelativePath), current.ManifestBytes.Length,
            current.ManifestHash, cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("ContentPackage.InvalidMediaBinding");
        return await PruneUnderLockAsync(ownedRoot, current.ManifestId + ".json",
            current.Manifest.Packages.Select(package => package.ContentHash).ToHashSet(StringComparer.OrdinalIgnoreCase), supersedesOlderGenerations: true,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes every package folder and media manifest of the kind when an update publishes no generation of it, with the same
    /// rules as <see cref="PruneAsync(WinPeContentPackageMediaLease, string, CancellationToken)"/>. Does nothing when the media root is absent.
    /// </summary>
    /// <exception cref="InvalidOperationException">The kind does not prune media on update.</exception>
    public async Task<ContentPackagePruneResult> PruneAllAsync(string root, CancellationToken cancellationToken)
    {
        if (!kind.PrunesMediaOnUpdate) throw new InvalidOperationException($"'{kind.Name}' media content is not pruned on update.");
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string ownedRoot = CustomImagePathPolicy.ResolveRelativePath(root, kind.MediaRoot);
        if (!Directory.Exists(ownedRoot)) return new(0, 0, []);
        using var publicationLock = new FileStream(CustomImagePathPolicy.ResolveRelativePath(ownedRoot, WinPeMediaFileTransfer.PublicationLockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        return await PruneUnderLockAsync(ownedRoot, null, new HashSet<string>(StringComparer.OrdinalIgnoreCase), supersedesOlderGenerations: true,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Takes the publication lock of the kind under <paramref name="root"/>, the one <see cref="PublishAsync"/> holds while it
    /// stages content and exposes its manifest. Returns <see langword="null"/> when the media root of the kind is absent.
    /// </summary>
    /// <exception cref="IOException">Another process holds the lock, for example a Foundry OSD publishing content of the kind.</exception>
    public FileStream? OpenPublicationLock(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string ownedRoot = CustomImagePathPolicy.ResolveRelativePath(root, kind.MediaRoot);
        if (!Directory.Exists(ownedRoot)) return null;
        return new FileStream(CustomImagePathPolicy.ResolveRelativePath(ownedRoot, WinPeMediaFileTransfer.PublicationLockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>
    /// Removes the package folders of the kind that no hash of <paramref name="referenced"/> names, from a root that several
    /// media generations share, such as a network cache share. No generation supersedes the others there, so a manifest is
    /// removed only when it describes at least one package and none of its packages remains; a manifest that still describes
    /// kept content stays. Runs for every kind with the proof rules of
    /// <see cref="PruneAsync(WinPeContentPackageMediaLease, string, CancellationToken)"/>. Does nothing when the media root is absent.
    /// </summary>
    /// <remarks>
    /// The caller holds the lock from <see cref="OpenPublicationLock"/> and computed <paramref name="referenced"/> under it.
    /// Package folders and manifests last written at or after <paramref name="keepWrittenSinceUtc"/> stay, as does every
    /// package such a manifest describes: their publisher may not have written the index that references them yet.
    /// </remarks>
    public async Task<ContentPackagePruneResult> PruneUnreferencedAsync(string root, IReadOnlySet<string> referenced, DateTime keepWrittenSinceUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(referenced);
        string ownedRoot = CustomImagePathPolicy.ResolveRelativePath(root, kind.MediaRoot);
        if (!Directory.Exists(ownedRoot)) return new(0, 0, []);
        ContentPackagePruneResult pruned = await PruneUnderLockAsync(ownedRoot, null, referenced.ToHashSet(StringComparer.OrdinalIgnoreCase),
            supersedesOlderGenerations: false, cancellationToken, keepWrittenSinceUtc).ConfigureAwait(false);
        List<string> retained = [.. pruned.RetainedUnknownEntries];
        RemoveOrphanedStaging(ownedRoot, retained, cancellationToken);
        return pruned with { RetainedUnknownEntries = retained };
    }

    /// <summary>
    /// Deletes the staging folders that an interrupted publication left. Publication stages under the publication lock, so
    /// while the caller holds that lock every staging folder is orphaned. A folder that holds a reparse point, or a file that
    /// cannot be deleted, stays and is reported.
    /// </summary>
    private void RemoveOrphanedStaging(string ownedRoot, List<string> retained, CancellationToken cancellationToken)
    {
        foreach (FileSystemInfo entry in Entries(ownedRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry is not DirectoryInfo folder || !folder.Name.StartsWith(".pending-", StringComparison.Ordinal)) continue;
            if (IsReparsePoint(folder) || ContainsReparsePoint(folder))
            {
                Retain(ownedRoot, folder.FullName, "ReparsePoint", retained);
                continue;
            }

            try
            {
                folder.Delete(recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Retain(ownedRoot, folder.FullName, "InUse", retained);
            }
        }
    }

    /// <summary>
    /// Removes packages first, then manifests, so a failed run keeps a description for anything left behind. When
    /// <paramref name="supersedesOlderGenerations"/> is set, the current generation replaces the older ones, so their manifests
    /// go once their unreferenced packages are gone; otherwise a manifest goes only once none of its packages remains. With
    /// <paramref name="keepWrittenSinceUtc"/>, manifests and package folders written since then stay, with every package those manifests describe.
    /// </summary>
    private async Task<ContentPackagePruneResult> PruneUnderLockAsync(string ownedRoot, string? currentManifestName, HashSet<string> referenced,
        bool supersedesOlderGenerations, CancellationToken cancellationToken, DateTime? keepWrittenSinceUtc = null)
    {
        List<string> retained = [];
        Dictionary<string, ContentPackageMediaManifest> previous = await ReadPreviousManifestsAsync(ownedRoot, currentManifestName, retained, cancellationToken).ConfigureAwait(false);
        if (keepWrittenSinceUtc is DateTime since)
        {
            foreach (string name in previous.Keys.ToList())
            {
                if (File.GetLastWriteTimeUtc(Path.Combine(ownedRoot, "manifests", name)) < since) continue;
                referenced.UnionWith(previous[name].Packages.Select(package => package.ContentHash));
                previous.Remove(name);
            }
        }

        // A shared root can have readers, such as devices staging a package from a network cache share: an entry in use stays.
        bool keepEntriesInUse = !supersedesOlderGenerations;
        int removedPackages = RemoveUnreferencedPackages(ownedRoot, referenced, previous.Values, keepEntriesInUse, keepWrittenSinceUtc, retained, cancellationToken);
        int removedManifests = supersedesOlderGenerations
            ? RemoveObsoleteManifests(ownedRoot, referenced, previous, keepEntriesInUse, cancellationToken)
            : RemoveObsoleteManifests(ownedRoot, [], previous.Where(pair => pair.Value.Packages.Count > 0).ToDictionary(StringComparer.OrdinalIgnoreCase),
                keepEntriesInUse, cancellationToken);
        return new(removedPackages, removedManifests, retained);
    }

    private async Task<Dictionary<string, ContentPackageMediaManifest>> ReadPreviousManifestsAsync(string ownedRoot, string? currentManifestName,
        List<string> retained, CancellationToken cancellationToken)
    {
        Dictionary<string, ContentPackageMediaManifest> manifests = new(StringComparer.OrdinalIgnoreCase);
        string directory = Path.Combine(ownedRoot, "manifests");
        if (!Directory.Exists(directory)) return manifests;
        ThrowIfReparsePoint(directory);
        var reader = new ContentPackageMediaReader(kind);
        foreach (FileSystemInfo entry in Entries(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Name.Equals(currentManifestName, StringComparison.OrdinalIgnoreCase)) continue;
            ContentPackageMediaManifest? manifest = null;
            if (entry is FileInfo file && !IsReparsePoint(file) && IsManifestName(entry.Name) && file.Length <= kind.MaximumMediaManifestBytes)
            {
                byte[] bytes = await File.ReadAllBytesAsync(file.FullName, cancellationToken).ConfigureAwait(false);
                if (bytes.Length <= kind.MaximumMediaManifestBytes) manifest = reader.Parse(bytes, entry.Name[..32]);
            }
            if (manifest is null) Retain(ownedRoot, entry.FullName, "UnrecognizedManifest", retained);
            else manifests[entry.Name] = manifest;
        }
        return manifests;
    }

    private int RemoveUnreferencedPackages(string ownedRoot, HashSet<string> referenced, IEnumerable<ContentPackageMediaManifest> previous,
        bool keepEntriesInUse, DateTime? keepWrittenSinceUtc, List<string> retained, CancellationToken cancellationToken)
    {
        string directory = Path.Combine(ownedRoot, "Packages");
        if (!Directory.Exists(directory)) return 0;
        ThrowIfReparsePoint(directory);
        Dictionary<string, ContentPackageManifest> descriptions = new(StringComparer.OrdinalIgnoreCase);
        foreach (ContentPackageMediaPackage package in previous.SelectMany(manifest => manifest.Packages)) descriptions.TryAdd(package.ContentHash, package.Manifest);
        int removed = 0;
        foreach (FileSystemInfo entry in Entries(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry is DirectoryInfo && (referenced.Contains(entry.Name) || entry.LastWriteTimeUtc >= keepWrittenSinceUtc)) continue;
            if (entry is not DirectoryInfo folder || !ContentPackagePathPolicy.IsValidHash(entry.Name) || !descriptions.TryGetValue(entry.Name, out ContentPackageManifest? description))
                Retain(ownedRoot, entry.FullName, "Undescribed", retained);
            else if (IsReparsePoint(folder) || ContainsReparsePoint(folder))
                Retain(ownedRoot, entry.FullName, "ReparsePoint", retained);
            else if (RemovePackage(ownedRoot, folder, description, keepEntriesInUse, retained)) removed++;
        }
        return removed;
    }

    /// <summary>
    /// Deletes the described files whose length still matches, then the described folders bottom-up while they are empty. With
    /// <paramref name="keepEntriesInUse"/>, a file or folder that cannot be deleted stays and is reported instead of failing the run.
    /// </summary>
    private bool RemovePackage(string ownedRoot, DirectoryInfo folder, ContentPackageManifest description, bool keepEntriesInUse, List<string> retained)
    {
        string files = Path.Combine(folder.FullName, "files");
        foreach (ContentPackageFile file in description.Files)
        {
            var info = new FileInfo(Path.Combine(files, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (info.Exists && !IsReparsePoint(info) && info.Length == file.Length) Delete(info, keepEntriesInUse);
        }
        string[] describedDirectories = description.Directories.Select(directory => Path.Combine(files, directory.Replace('/', Path.DirectorySeparatorChar)))
            .OrderByDescending(directory => directory.Length).Append(files).ToArray();
        foreach (string directory in describedDirectories.Append(folder.FullName))
        {
            var info = new DirectoryInfo(directory);
            if (info.Exists && !IsReparsePoint(info) && !Entries(directory).Any()) Delete(info, keepEntriesInUse);
        }
        if (!Directory.Exists(folder.FullName)) return true;
        RetainLeftovers(ownedRoot, folder.FullName, describedDirectories.ToHashSet(StringComparer.OrdinalIgnoreCase), retained);
        return false;
    }

    private void RetainLeftovers(string ownedRoot, string directory, HashSet<string> describedDirectories, List<string> retained)
    {
        foreach (FileSystemInfo entry in Entries(directory))
        {
            if (entry is DirectoryInfo && !IsReparsePoint(entry) && describedDirectories.Contains(entry.FullName))
                RetainLeftovers(ownedRoot, entry.FullName, describedDirectories, retained);
            else
                Retain(ownedRoot, entry.FullName, "ChangedOrUndescribed", retained);
        }
    }

    private static int RemoveObsoleteManifests(string ownedRoot, HashSet<string> referenced, Dictionary<string, ContentPackageMediaManifest> previous,
        bool keepEntriesInUse, CancellationToken cancellationToken)
    {
        int removed = 0;
        foreach ((string name, ContentPackageMediaManifest manifest) in previous)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!manifest.Packages.All(package => referenced.Contains(package.ContentHash) || !Path.Exists(Path.Combine(ownedRoot, "Packages", package.ContentHash))))
                continue;
            var file = new FileInfo(Path.Combine(ownedRoot, "manifests", name));
            if (!file.Exists || IsReparsePoint(file) || !Delete(file, keepEntriesInUse)) continue;
            removed++;
        }
        return removed;
    }

    /// <summary>Deletes an entry; with <paramref name="keepEntriesInUse"/>, returns false instead of throwing when it is in use or protected.</summary>
    private static bool Delete(FileSystemInfo entry, bool keepEntriesInUse)
    {
        try
        {
            entry.Delete();
            return true;
        }
        catch (Exception exception) when (keepEntriesInUse && exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Retain(string ownedRoot, string path, string reason, List<string> retained)
    {
        string relative = $"{kind.MediaRoot}/{Path.GetRelativePath(ownedRoot, path).Replace(Path.DirectorySeparatorChar, '/')}";
        retained.Add(relative);
        Log.ForContext<WinPeContentPackageMediaService>().Debug(
            "Content package pruning kept an unproven media entry. Kind={Kind} Path={Path} Reason={Reason}", kind.Name, relative, reason);
    }

    private static List<FileSystemInfo> Entries(string directory) => new DirectoryInfo(directory).EnumerateFileSystemInfos("*", AllEntries).ToList();

    private static bool ContainsReparsePoint(DirectoryInfo directory) =>
        Entries(directory.FullName).Any(entry => IsReparsePoint(entry) || (entry is DirectoryInfo child && ContainsReparsePoint(child)));

    private static bool IsReparsePoint(FileSystemInfo entry) => (entry.Attributes & FileAttributes.ReparsePoint) != 0;

    private static void ThrowIfReparsePoint(string directory)
    {
        if (IsReparsePoint(new DirectoryInfo(directory))) throw new InvalidDataException("ContentPackage.RedirectedPackagePath");
    }

    private static bool IsManifestName(string name) =>
        name.Length == 37 && name.EndsWith(".json", StringComparison.Ordinal) && name[..32].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private void ThrowIfInvalidLease(WinPeContentPackageMediaLease package)
    {
        ArgumentNullException.ThrowIfNull(package);
        package.ThrowIfDisposed();
        if (package.Kind != kind) throw new ArgumentException($"The lease holds '{package.Kind.Name}' packages but this service publishes '{kind.Name}' packages.", nameof(package));
    }

    private static async Task<bool> CopyAndReadBackAsync(WinPeContentPackageMediaFile file, string temporary, WinPeMediaByteProgress byteProgress,
        CancellationToken cancellationToken)
    {
        await WinPeMediaFileTransfer.CopyAsync(file.SourcePath, temporary, byteProgress.Half, cancellationToken).ConfigureAwait(false);
        return await MatchesAsync(temporary, file.Length, file.ContentHash, cancellationToken, byteProgress.Half).ConfigureAwait(false);
    }

    private static async Task<bool> MatchesAsync(string path, long length, string hash, CancellationToken cancellationToken,
        Action<long>? onBytes = null)
    {
        CustomImagePathPolicy.ValidateNoReparsePoints(path);
        if (!File.Exists(path)) return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
        if (stream.Length != length) return false;
        string actual = onBytes is null
            ? Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false))
            : await WinPeMediaFileTransfer.HashAsync(stream, onBytes, cancellationToken).ConfigureAwait(false);
        return actual.Equals(hash, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Publishes a prepared content package lease to external media.</summary>
internal interface IWinPeContentPackageMediaPublisher
{
    /// <summary>Verifies that every held source file still matches its recorded identity.</summary>
    Task ValidateSourcesAsync(WinPeContentPackageMediaLease package, CancellationToken cancellationToken);
    /// <summary>Returns the bytes the media volume needs to publish the lease.</summary>
    Task<long> GetRequiredBytesAsync(WinPeContentPackageMediaLease package, string root, CancellationToken cancellationToken);
    /// <summary>Throws when the media volume cannot hold the lease plus additional bytes.</summary>
    Task ValidateCapacityAsync(WinPeContentPackageMediaLease package, string root, long additionalBytes, CancellationToken cancellationToken);
    /// <summary>Copies the lease content to the media root and exposes its manifest last.</summary>
    Task PublishAsync(WinPeContentPackageMediaLease package, string root, CancellationToken cancellationToken, IProgress<WinPeMediaProgress>? progress = null);
    /// <summary>Removes media content that only older generations reference; throws for kinds that do not prune.</summary>
    Task<ContentPackagePruneResult> PruneAsync(WinPeContentPackageMediaLease current, string root, CancellationToken cancellationToken);
    /// <summary>Removes all media content of the kind when the update publishes no generation of it; throws for kinds that do not prune.</summary>
    Task<ContentPackagePruneResult> PruneAllAsync(string root, CancellationToken cancellationToken);
}
