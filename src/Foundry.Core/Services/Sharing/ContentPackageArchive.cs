// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Packages;
using Foundry.Core.Services.Packages;
using Foundry.Utilities.IO;

namespace Foundry.Core.Services.Sharing;

/// <summary>
/// Turns one library package (post-installation package or driver set) into a deterministic ZIP archive for publication and imports
/// such an archive back into a library (spec 5.5). One code path serves every <see cref="ContentPackageKind"/>.
/// </summary>
/// <remarks>
/// Archive layout: <c>manifest.json</c> (the canonical library manifest, whose SHA-256 is the content hash) followed by one
/// <c>files/&lt;relative path&gt;</c> entry per file, sorted ordinally, stored without compression, every timestamp
/// <see cref="EntryTimestamp"/>. Empty directories travel in the manifest only. The same package always produces the same bytes.
/// </remarks>
public static class ContentPackageArchive
{
    private const string ManifestEntryName = "manifest.json";
    private const string FilesPrefix = "files/";
    private const int CopyBufferSize = 128 * 1024;

    /// <summary>
    /// Most entries an extraction directory may hold when its lease removes it: above every kind's file limit (20,000 driver files)
    /// plus their directories.
    /// </summary>
    private const int ExtractionMaximumEntries = 100_000;

    /// <summary>
    /// Fixed entry timestamp. The DOS time stored is the clock time of this offset, so a UTC offset keeps the bytes identical in every
    /// time zone. Read-back timestamps carry the local offset and are never checked.
    /// </summary>
    private static readonly DateTimeOffset EntryTimestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Writes the archive of a library package. The package is read through a library lease, which verifies it and blocks its
    /// deletion until the archive is written; file contents are streamed, never held in memory.
    /// </summary>
    /// <param name="library">Library that holds the package.</param>
    /// <param name="contentHash">Content hash of the package.</param>
    /// <param name="destination">
    /// Seekable stream that receives the archive from its current position (a non-seekable stream would make the ZIP writer add data
    /// descriptors and change the bytes). It is left open.
    /// </param>
    /// <param name="cancellationToken">Stops between copy chunks.</param>
    /// <param name="progress">
    /// Receives the cumulative bytes handled: the verification of the package files by the lease, then their copy into the archive,
    /// so it ends at twice the package length.
    /// </param>
    /// <exception cref="ArgumentException">The destination is not seekable.</exception>
    /// <exception cref="InvalidDataException">The package is missing or damaged in the library.</exception>
    public static async Task WriteAsync(ContentPackageLibraryService library, string contentHash, Stream destination, CancellationToken cancellationToken,
        IProgress<long>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanSeek)
        {
            throw new ArgumentException("The archive destination must be seekable.", nameof(destination));
        }

        await using ContentPackageLease lease = await library.AcquireAsync(contentHash, cancellationToken, progress).ConfigureAwait(false);
        long copied = lease.Files.Sum(file => file.Length);
        byte[] manifest = ContentPackageManifestCodec.Serialize(lease.Manifest);
        await using ZipArchive archive = await ZipArchive.CreateAsync(destination, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken)
            .ConfigureAwait(false);
        await using (Stream entry = await CreateEntryAsync(archive, ManifestEntryName, cancellationToken).ConfigureAwait(false))
        {
            await entry.WriteAsync(manifest, cancellationToken).ConfigureAwait(false);
        }

        foreach (ContentPackageSourceFile file in lease.Files.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            await using FileStream input = new(file.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using Stream entry = await CreateEntryAsync(archive, FilesPrefix + file.RelativePath, cancellationToken).ConfigureAwait(false);
            long offset = copied;
            copied += await StreamCopy.CopyAsync(input, entry, progress is null ? null : bytes => progress.Report(offset + bytes), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Imports an archive written by <see cref="WriteAsync"/> into <paramref name="library"/>. Every entry name is checked against
    /// the library kind's <see cref="ContentPackagePathPolicy"/> before anything is extracted; the manifest must hash to
    /// <paramref name="expectedHash"/> (the library content hash is the SHA-256 of that canonical manifest) and every file must match
    /// it. The files are extracted into a temporary directory of the current user and imported through
    /// <see cref="ContentPackageLibraryService.ImportAsync(string, CancellationToken)"/>, so the kind rules apply (for drivers:
    /// 150-character relative paths, 20,000 files, at least one INF file, and full-path limits on the library destination); the
    /// resulting hash is compared with <paramref name="expectedHash"/> again. Nothing is added to the library on failure.
    /// </summary>
    /// <param name="library">Target library.</param>
    /// <param name="archive">Seekable archive stream (a non-seekable one would be buffered whole by the ZIP reader); left open.</param>
    /// <param name="expectedHash">Content hash listed in the publication.</param>
    /// <param name="cancellationToken">Stops between copy chunks; the temporary directory is removed.</param>
    /// <returns>The library reference; its display name is the content hash, the caller names it.</returns>
    /// <exception cref="ArgumentException">The archive is not seekable.</exception>
    /// <exception cref="InvalidDataException">
    /// An entry name is unsafe, duplicated or outside <c>manifest.json</c> and <c>files/</c>, the archive does not match its manifest,
    /// the manifest does not hash to <paramref name="expectedHash"/>, or the package breaks the library kind rules.
    /// </exception>
    public static Task<ContentPackageReference> ImportAsync(ContentPackageLibraryService library, Stream archive, string expectedHash,
        CancellationToken cancellationToken) =>
        ImportAsync(library, archive, expectedHash, temporaryRoot: null, cancellationToken);

    /// <inheritdoc cref="ImportAsync(ContentPackageLibraryService, Stream, string, CancellationToken)"/>
    /// <param name="library">Target library.</param>
    /// <param name="archive">Seekable archive stream; left open.</param>
    /// <param name="expectedHash">Content hash listed in the publication.</param>
    /// <param name="temporaryRoot">
    /// Dedicated root of the owner-only extraction directories (<see cref="PrivateDirectory"/>); defaults to
    /// <c>%TEMP%\Foundry\PackageImport</c>. The extraction directory is removed on every path, and abandoned ones under the root are
    /// removed by the next import.
    /// </param>
    /// <param name="cancellationToken">Stops between copy chunks; the extraction directory is removed.</param>
    /// <param name="progress">
    /// Receives the cumulative bytes handled: the checked extraction of the package files, then their import into the library, so it
    /// ends at twice the package length.
    /// </param>
    /// <exception cref="IOException">The extraction root is redirected or its cleanup lock is held by another process.</exception>
    public static async Task<ContentPackageReference> ImportAsync(ContentPackageLibraryService library, Stream archive, string expectedHash,
        string? temporaryRoot, CancellationToken cancellationToken, IProgress<long>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(archive);
        if (!archive.CanSeek)
        {
            throw new ArgumentException("The archive must be seekable.", nameof(archive));
        }

        if (!ContentPackagePathPolicy.IsValidHash(expectedHash))
        {
            throw new InvalidDataException("ContentPackage.InvalidPackageReference");
        }

        string hash = expectedHash.ToLowerInvariant();

        // The library refuses sources inside its own root, so the files are extracted into an owner-only leased directory under the
        // temporary root (ContentSync passes its own, on the volume of the libraries). The library also checks source paths against
        // the kind's full-path limit while it inventories them (ruling R10). That check never rejects a package the destination
        // accepts, as long as the extraction prefix (root, then <32 hex>\package\) is shorter than the destination prefix (library
        // root, then content\<64 hex>\files\), which holds for the default %TEMP% root and for the per-user sharing root.
        PrivateDirectoryLease lease = PrivateDirectory.CreateLeased(
            temporaryRoot ?? Path.Combine(Path.GetTempPath(), "Foundry", "PackageImport"), ExtractionMaximumEntries);

        // A subdirectory: the leased directory also holds its lease file, which must not become package content.
        string extracted = Path.Combine(lease.Path, "package");
        long extractedBytes;
        try
        {
            await using (ZipArchive zip = await ZipArchive.CreateAsync(archive, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null, cancellationToken)
                .ConfigureAwait(false))
            {
                extractedBytes = await ExtractAsync(zip, library.Kind, hash, extracted, progress, cancellationToken).ConfigureAwait(false);
            }

            long offset = extractedBytes;
            IProgress<ContentPackageImportProgress>? imported = progress is null ? null : new LibraryImportProgress(progress, offset);
            return await ImportExtractedAsync(library, extracted, hash, cancellationToken, imported).ConfigureAwait(false);
        }
        finally
        {
            ReleaseBestEffort(lease);
        }
    }

    /// <summary>
    /// Imports an extracted package directory and checks that the library computed <paramref name="expectedHash"/>. The extraction
    /// already verified the manifest hash and every file, so a mismatch means the directory changed during the import; the entry the
    /// import created is then removed, while an entry that existed before the import is never touched.
    /// </summary>
    internal static async Task<ContentPackageReference> ImportExtractedAsync(ContentPackageLibraryService library, string directory, string expectedHash,
        CancellationToken cancellationToken, IProgress<ContentPackageImportProgress>? progress = null)
    {
        IReadOnlySet<string> existing = library.ListContentHashes();
        ContentPackageReference reference = await library.ImportAsync(directory, progress, cancellationToken).ConfigureAwait(false);
        if (reference.ContentHash.Equals(expectedHash, StringComparison.Ordinal))
        {
            return reference with { DisplayName = reference.ContentHash };
        }

        if (!existing.Contains(reference.ContentHash))
        {
            try
            {
                await library.DeleteAsync(reference.ContentHash, [], CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // The mismatch below is the error to report; a leftover entry is unreferenced and can be deleted later.
            }
        }

        throw new InvalidDataException("ContentPackage.InvalidPackageArchive");
    }

    /// <summary>
    /// Returns where a manifest path is extracted. The path syntax was validated by the kind policy (no rooted names, no <c>..</c>,
    /// forward slashes only), so it stays inside <paramref name="root"/>; full-path limits are left to the library, which checks them on
    /// its own destination.
    /// </summary>
    private static string ExtractionPath(string root, string relativePath) => Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Removes the extraction directory; a leftover is removed by the next import's abandoned cleanup.</summary>
    private static void ReleaseBestEffort(PrivateDirectoryLease lease)
    {
        try
        {
            lease.Dispose();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The leftover must not replace the import's own outcome.
        }
    }

    private static async Task<Stream> CreateEntryAsync(ZipArchive archive, string name, CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = EntryTimestamp;
        return await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates every entry name, then the manifest, then extracts the manifest's directories and files into <paramref name="root"/>,
    /// reporting the cumulative bytes extracted; returns the package length.
    /// </summary>
    private static async Task<long> ExtractAsync(ZipArchive zip, ContentPackageKind kind, string hash, string root, IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        ContentPackagePathPolicy policy = new(kind);
        if (zip.Entries.Count > kind.MaximumFiles + 1)
        {
            throw new InvalidDataException("ContentPackage.PackageTooManyFiles");
        }

        ZipArchiveEntry? manifestEntry = null;
        Dictionary<string, ZipArchiveEntry> files = new(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (entry.FullName == ManifestEntryName && manifestEntry is null)
            {
                manifestEntry = entry;
                continue;
            }

            if (!entry.FullName.StartsWith(FilesPrefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException("ContentPackage.InvalidPackageArchive");
            }

            string relativePath = entry.FullName[FilesPrefix.Length..];
            policy.ValidateRelativePath(relativePath);
            if (!files.TryAdd(relativePath, entry))
            {
                throw new InvalidDataException("ContentPackage.DuplicatePackagePath");
            }
        }

        ContentPackageManifest manifest = await ReadManifestAsync(manifestEntry, hash, kind, cancellationToken).ConfigureAwait(false);
        if (manifest.Files.Count != files.Count || manifest.Files.Any(file => !files.TryGetValue(file.RelativePath, out ZipArchiveEntry? entry) || entry.FullName != FilesPrefix + file.RelativePath))
        {
            throw new InvalidDataException("ContentPackage.InvalidPackageArchive");
        }

        Directory.CreateDirectory(root);
        foreach (string directory in manifest.Directories)
        {
            Directory.CreateDirectory(ExtractionPath(root, directory));
        }

        byte[] buffer = new byte[CopyBufferSize];
        long extracted = 0;
        foreach (ContentPackageFile file in manifest.Files)
        {
            long offset = extracted;
            await ExtractFileAsync(files[file.RelativePath], file, ExtractionPath(root, file.RelativePath), buffer,
                progress is null ? null : bytes => progress.Report(offset + bytes), cancellationToken).ConfigureAwait(false);
            extracted += file.Length;
        }

        return extracted;
    }

    private static async Task<ContentPackageManifest> ReadManifestAsync(ZipArchiveEntry? entry, string hash, ContentPackageKind kind, CancellationToken cancellationToken)
    {
        if (entry is null || entry.Length is 0 or > ContentPackageManifestCodec.MaximumManifestBytes)
        {
            throw new InvalidDataException("ContentPackage.InvalidPackageArchive");
        }

        byte[] bytes = new byte[entry.Length];
        await using (Stream input = await entry.OpenAsync(cancellationToken).ConfigureAwait(false))
        {
            await input.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        return ContentPackageManifestCodec.Deserialize(bytes, hash, kind);
    }

    /// <summary>Copies one entry, rejecting it unless its length and SHA-256 match the manifest.</summary>
    private static async Task ExtractFileAsync(ZipArchiveEntry entry, ContentPackageFile file, string target, byte[] buffer, Action<long>? reportBytes,
        CancellationToken cancellationToken)
    {
        if (entry.Length != file.Length)
        {
            throw new InvalidDataException("ContentPackage.InvalidPackageArchive");
        }

        using IncrementalHash sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long copied = 0;
        await using (Stream input = await entry.OpenAsync(cancellationToken).ConfigureAwait(false))
        await using (FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, FileOptions.Asynchronous))
        {
            int count;
            while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                copied += count;
                if (copied > file.Length)
                {
                    throw new InvalidDataException("ContentPackage.InvalidPackageArchive");
                }

                sha256.AppendData(buffer, 0, count);
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                reportBytes?.Invoke(copied);
            }
        }

        if (copied != file.Length || !Convert.ToHexStringLower(sha256.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("ContentPackage.InvalidPackageArchive");
        }
    }

    /// <summary>Maps the copy-and-verify progress of the library import onto the bytes after the extraction.</summary>
    private sealed class LibraryImportProgress(IProgress<long> target, long offset) : IProgress<ContentPackageImportProgress>
    {
        public void Report(ContentPackageImportProgress value) => target.Report(offset + value.CompletedBytes);
    }
}
