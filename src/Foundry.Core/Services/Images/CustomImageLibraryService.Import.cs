// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Serilog;

namespace Foundry.Core.Services.Images;

public sealed partial class CustomImageLibraryService
{
    /// <summary>Streams imports to a private pending directory and publishes only fully inspected content.</summary>
    public async Task<CustomImageReference> ImportAsync(CustomImageImportRequest request,
        IProgress<CustomImageImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string name = CustomImageSettingsValidator.NormalizeDisplayName(request.DisplayName);
        cancellationToken.ThrowIfCancellationRequested();
        string pending = OwnedPath($"pending/{Guid.NewGuid():N}");
        using FileStream operationLease = PreparePendingImport(pending);
        Log.ForContext<CustomImageLibraryService>().Information("Custom image import started.");
        try
        {
            await using CustomImageImportSource source = await CustomImageImportSource.OpenAsync(request.SourcePath, cancellationToken, request.IsoImagePath).ConfigureAwait(false);
            string stagedWim = Path.Combine(pending, "image.wim");
            await using (FileStream sourceLease = OpenRead(source.ImagePath))
            {
                if (Path.GetExtension(source.ImagePath).Equals(".esd", StringComparison.OrdinalIgnoreCase))
                {
                    IReadOnlyList<CustomImageIndex> esdIndexes = await metadataReader.ReadAsync(source.ImagePath, cancellationToken).ConfigureAwait(false);
                    if (esdIndexes.Count is 0 or > CustomImageSettingsValidator.MaximumIndexes)
                        throw new InvalidDataException("The source image does not contain usable image indexes.");
                    var exportVolume = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(stagedWim))!);
                    CustomImageExportCapacityPolicy.Validate(esdIndexes, exportVolume.AvailableFreeSpace, exportVolume.DriveFormat);
                    foreach (CustomImageIndex index in esdIndexes)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        progress?.Report(new("Exporting", index.Index - 1, esdIndexes.Count));
                        await CustomImageImportSource.ExportIndexAsync(source.ImagePath, stagedWim, index.Index, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    await CopyAsync(sourceLease, stagedWim, progress, cancellationToken).ConfigureAwait(false);
                }
            }

            IReadOnlyList<CustomImageIndex> indexes;
            string imageHash;
            long length;
            await using (FileStream stagedLease = OpenRead(stagedWim))
            {
                progress?.Report(new("Inspecting", 0, stagedLease.Length));
                indexes = await metadataReader.ReadAsync(stagedWim, cancellationToken).ConfigureAwait(false);
                length = stagedLease.Length;
                imageHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stagedLease, cancellationToken).ConfigureAwait(false));
            }
            var reference = new CustomImageReference
            {
                Id = Guid.NewGuid().ToString("N"),
                ContentHash = imageHash,
                DisplayName = name,
                Length = length,
                Indexes = indexes
            };
            if (!CustomImageSettingsValidator.IsValidReference(reference))
                throw new InvalidDataException("The source does not contain valid readable image metadata.");
            cancellationToken.ThrowIfCancellationRequested();
            reference = await PublishStagedAsync(stagedWim, reference, cancellationToken).ConfigureAwait(false);
            progress?.Report(new("Completed", length, length));
            Log.ForContext<CustomImageLibraryService>().Information("Custom image import completed. IndexCount={IndexCount}, ImageBytes={ImageBytes}", indexes.Count, length);
            return reference;
        }
        catch (OperationCanceledException)
        {
            Log.ForContext<CustomImageLibraryService>().Information("Custom image import canceled.");
            throw;
        }
        catch (Exception exception)
        {
            Log.ForContext<CustomImageLibraryService>().Error(exception, "Custom image import failed.");
            throw;
        }
        finally
        {
            operationLease.Dispose();
            try { DeleteOwnedDirectory(pending, rootDirectory); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.ForContext<CustomImageLibraryService>().Warning(exception, "Custom image pending content cleanup failed.");
            }
        }
    }

    /// <summary>
    /// Imports an image fetched from a publication (spec 5.5). <paramref name="wim"/> is streamed from its current position into a
    /// private pending directory and must hold exactly <see cref="CustomImageReference.Length"/> bytes hashing to
    /// <see cref="CustomImageReference.ContentHash"/>; the metadata reader must then find the reference's index numbers, as
    /// <see cref="AcquireAsync"/> requires, and the entry keeps the indexes it read. The content is published like
    /// <see cref="ImportAsync"/>: an entry with the same hash keeps its id, and nothing is added to the library on failure.
    /// </summary>
    /// <param name="wim">Image bytes; read to the end, left open.</param>
    /// <param name="reference">Published reference of the image (its id and display name become the library entry's).</param>
    /// <param name="ct">Stops between copy chunks; pending content is removed.</param>
    /// <exception cref="InvalidDataException">The reference is invalid, or the image does not match its length, hash or indexes.</exception>
    /// <exception cref="IOException">The library volume has not enough free space, or the library cannot be written.</exception>
    public async Task ImportFromPublicationAsync(Stream wim, CustomImageReference reference, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(wim);
        ArgumentNullException.ThrowIfNull(reference);
        if (!CustomImageSettingsValidator.IsValidReference(reference)) throw new InvalidDataException("The custom image reference is invalid.");
        ct.ThrowIfCancellationRequested();
        string pending = OwnedPath($"pending/{Guid.NewGuid():N}");
        using FileStream operationLease = PreparePendingImport(pending);
        Log.ForContext<CustomImageLibraryService>().Information("Custom image publication import started. ImageBytes={ImageBytes}", reference.Length);
        try
        {
            string stagedWim = Path.Combine(pending, "image.wim");
            string imageHash = await CopyExactAsync(wim, stagedWim, reference.Length, ct).ConfigureAwait(false);
            if (!imageHash.Equals(reference.ContentHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The published image does not match its reference.");
            IReadOnlyList<CustomImageIndex> indexes;
            await using (FileStream stagedLease = OpenRead(stagedWim))
            {
                indexes = await metadataReader.ReadAsync(stagedWim, ct).ConfigureAwait(false);
            }
            CustomImageReference imported = reference with { ContentHash = imageHash, Indexes = indexes };
            if (!CustomImageSettingsValidator.IsValidReference(imported) ||
                !reference.Indexes.Select(index => index.Index).SequenceEqual(indexes.Select(index => index.Index)))
                throw new InvalidDataException("The image indexes do not match their imported metadata.");
            ct.ThrowIfCancellationRequested();
            await PublishStagedAsync(stagedWim, imported, ct).ConfigureAwait(false);
            Log.ForContext<CustomImageLibraryService>().Information("Custom image publication import completed. IndexCount={IndexCount}, ImageBytes={ImageBytes}",
                indexes.Count, reference.Length);
        }
        catch (OperationCanceledException)
        {
            Log.ForContext<CustomImageLibraryService>().Information("Custom image publication import canceled.");
            throw;
        }
        catch (InvalidDataException exception)
        {
            // A published image that does not match its reference is an expected rejection; the caller reports it as failed content.
            Log.ForContext<CustomImageLibraryService>().Warning(exception, "Custom image publication import rejected.");
            throw;
        }
        catch (Exception exception)
        {
            Log.ForContext<CustomImageLibraryService>().Error(exception, "Custom image publication import failed.");
            throw;
        }
        finally
        {
            operationLease.Dispose();
            try { DeleteOwnedDirectory(pending, rootDirectory); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.ForContext<CustomImageLibraryService>().Warning(exception, "Custom image pending content cleanup failed.");
            }
        }
    }

    /// <summary>
    /// Moves a fully inspected staged image into <c>content/&lt;hash&gt;/image.wim</c> and records it in the index under the library
    /// lock. An entry with the same hash keeps its id; an existing image that no longer verifies is replaced; on failure only content
    /// this call published is rolled back.
    /// </summary>
    /// <returns><paramref name="reference"/>, with the id of the existing entry when there is one.</returns>
    private async Task<CustomImageReference> PublishStagedAsync(string stagedWim, CustomImageReference reference, CancellationToken cancellationToken)
    {
        string imageHash = reference.ContentHash;
        using FileStream libraryLock = AcquireLibraryLock();
        IReadOnlyList<CustomImageReference> current = await ListAsync(cancellationToken).ConfigureAwait(false);
        CustomImageReference? existing = current.FirstOrDefault(image =>
            image.ContentHash == imageHash);
        if (existing is not null)
        {
            reference = reference with { Id = existing.Id };
        }
        if (existing is null && current.Count >= CustomImageSettingsValidator.MaximumImages)
            throw new InvalidDataException("The custom image library has reached its image limit.");
        byte[] indexBytes = SerializeIndex(current.Where(image => image.Id != reference.Id).Append(reference).ToArray());
        cancellationToken.ThrowIfCancellationRequested();
        string contentDirectory = OwnedPath($"content/{imageHash}");
        bool createdContentDirectory = !Directory.Exists(contentDirectory);
        Directory.CreateDirectory(contentDirectory);
        string destination = Path.Combine(contentDirectory, "image.wim");
        bool publishedUnreferencedContent = false;
        try
        {
            if (File.Exists(destination))
            {
                try
                {
                    await using FileStream existingImage = OpenRead(destination);
                    await VerifyAsync(existingImage, reference.Length, imageHash, cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidDataException)
                {
                    File.Move(stagedWim, destination, overwrite: true);
                    publishedUnreferencedContent = existing is null;
                }
            }
            else
            {
                File.Move(stagedWim, destination);
                publishedUnreferencedContent = existing is null;
            }

            await WriteIndexAsync(indexBytes, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Roll back only this import's unreferenced content while still holding the library lock.
            if (publishedUnreferencedContent)
            {
                try
                {
                    CustomImagePathPolicy.ValidateNoReparsePoints(destination);
                    CustomImagePathPolicy.ValidateNoReparsePoints(stagedWim);
                    File.Move(destination, stagedWim);
                    if (createdContentDirectory) Directory.Delete(contentDirectory, recursive: false);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    Log.ForContext<CustomImageLibraryService>().Warning(exception, "Custom image publication rollback failed.");
                }
            }
            throw;
        }
        return reference;
    }

    private FileStream PreparePendingImport(string pending)
    {
        using FileStream libraryLock = AcquireLibraryLock();
        string pendingRoot = OwnedPath("pending");
        Directory.CreateDirectory(pendingRoot);
        foreach (string directory in Directory.EnumerateDirectories(pendingRoot).Take(256))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) continue;
            try
            {
                CustomImagePathPolicy.ValidateNoReparsePoints(directory);
                using (new FileStream(Path.Combine(directory, ".lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
                DeleteOwnedDirectory(directory, rootDirectory);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Log.ForContext<CustomImageLibraryService>().Debug("A pending image import remains in use or cannot be cleaned up.");
            }
        }
        Directory.CreateDirectory(pending);
        return new FileStream(Path.Combine(pending, ".lease"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>Copies exactly <paramref name="length"/> bytes from the rest of <paramref name="input"/> and returns their lowercase SHA-256.</summary>
    /// <exception cref="InvalidDataException">The input holds fewer or more bytes than <paramref name="length"/>.</exception>
    private static async Task<string> CopyExactAsync(Stream input, string destination, long length, CancellationToken cancellationToken)
    {
        long available = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(destination))!).AvailableFreeSpace;
        if (length > available) throw new IOException("There is insufficient space to import the image source.");
        using IncrementalHash sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[1024 * 1024];
        long copied = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            copied += count;
            if (copied > length) throw new InvalidDataException("The published image does not match its reference.");
            sha256.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }
        if (copied != length) throw new InvalidDataException("The published image does not match its reference.");
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
        return Convert.ToHexStringLower(sha256.GetHashAndReset());
    }

    private static async Task CopyAsync(FileStream input, string destination, IProgress<CustomImageImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        long available = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(destination))!).AvailableFreeSpace;
        if (input.Length > available) throw new IOException("There is insufficient space to import the image source.");
        await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[1024 * 1024];
        long copied = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            copied += count;
            progress?.Report(new("Copying", copied, input.Length));
        }
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }
}
