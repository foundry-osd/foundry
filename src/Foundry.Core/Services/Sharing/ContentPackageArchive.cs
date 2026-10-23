// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Packages;
using Foundry.Core.Services.Packages;

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
    /// <exception cref="ArgumentException">The destination is not seekable.</exception>
    /// <exception cref="InvalidDataException">The package is missing or damaged in the library.</exception>
    public static async Task WriteAsync(ContentPackageLibraryService library, string contentHash, Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanSeek)
        {
            throw new ArgumentException("The archive destination must be seekable.", nameof(destination));
        }

        await using ContentPackageLease lease = await library.AcquireAsync(contentHash, cancellationToken).ConfigureAwait(false);
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
            await input.CopyToAsync(entry, CopyBufferSize, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Imports an archive written by <see cref="WriteAsync"/> into <paramref name="library"/>. Every entry name is checked against
    /// the library kind's <see cref="ContentPackagePathPolicy"/> before anything is extracted; the manifest must hash to
    /// <paramref name="expectedHash"/> (the library content hash is the SHA-256 of that canonical manifest) and every file must match
    /// it. The files are extracted into a temporary directory of the current user and imported through <see cref="ContentPackageLibraryService.ImportAsync(string, CancellationToken)"/>, so the kind rules apply
    /// (for drivers: 150-character relative paths, 20,000 files, at least one INF file). Nothing is added to the library on failure.
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
    public static async Task<ContentPackageReference> ImportAsync(ContentPackageLibraryService library, Stream archive, string expectedHash,
        CancellationToken cancellationToken)
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

        // The library refuses sources inside its own root, so the files are extracted under the user's temporary folder. The prefix
        // stays short because the kind's full-path limit (259 characters for post-installation packages) also applies here.
        string extracted = Path.Combine(Path.GetTempPath(), "Foundry", Guid.NewGuid().ToString("N"));
        try
        {
            await using (ZipArchive zip = await ZipArchive.CreateAsync(archive, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null, cancellationToken)
                .ConfigureAwait(false))
            {
                await ExtractAsync(zip, library.Kind, hash, extracted, cancellationToken).ConfigureAwait(false);
            }

            // The extracted tree holds exactly the manifest's directories and verified files, so the library computes the same
            // canonical manifest and therefore the expected hash; a mismatch was already rejected before anything reached the library.
            ContentPackageReference reference = await library.ImportAsync(extracted, cancellationToken).ConfigureAwait(false);
            return reference with { DisplayName = reference.ContentHash };
        }
        finally
        {
            if (Directory.Exists(extracted))
            {
                Directory.Delete(extracted, recursive: true);
            }
        }
    }

    private static async Task<Stream> CreateEntryAsync(ZipArchive archive, string name, CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = EntryTimestamp;
        return await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Validates every entry name, then the manifest, then extracts the manifest's directories and files into <paramref name="root"/>.</summary>
    private static async Task ExtractAsync(ZipArchive zip, ContentPackageKind kind, string hash, string root, CancellationToken cancellationToken)
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
            _ = policy.ResolveLexically(root, relativePath);
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
            Directory.CreateDirectory(policy.Resolve(root, directory));
        }

        byte[] buffer = new byte[CopyBufferSize];
        foreach (ContentPackageFile file in manifest.Files)
        {
            await ExtractFileAsync(files[file.RelativePath], file, policy.Resolve(root, file.RelativePath), buffer, cancellationToken).ConfigureAwait(false);
        }
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
    private static async Task ExtractFileAsync(ZipArchiveEntry entry, ContentPackageFile file, string target, byte[] buffer, CancellationToken cancellationToken)
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
            }
        }

        if (copied != file.Length || !Convert.ToHexStringLower(sha256.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("ContentPackage.InvalidPackageArchive");
        }
    }
}
