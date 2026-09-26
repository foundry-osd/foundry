// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Services.Images;

/// <summary>Imports immutable local image content and acquires verified source leases for media builds.</summary>
public sealed partial class CustomImageLibraryService
{
    private const int MaximumMetadataBytes = 8 * 1024 * 1024;
    private readonly string rootDirectory;
    private readonly ICustomImageMetadataReader metadataReader;

    public CustomImageLibraryService(string rootDirectory, ICustomImageMetadataReader? metadataReader = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        this.rootDirectory = Path.GetFullPath(rootDirectory);
        this.metadataReader = metadataReader ?? new NativeCustomImageMetadataReader();
    }

    /// <summary>Checks image presence and size for authoring readiness; builds still require verified leases.</summary>
    public bool IsAvailable(CustomImageReference reference)
    {
        if (reference is null || !CustomImageSettingsValidator.IsValidReference(reference)) return false;
        try
        {
            string imagePath = OwnedPath($"content/{reference.ContentHash.ToLowerInvariant()}/image.wim");
            return File.Exists(imagePath) && new FileInfo(imagePath).Length == reference.Length;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Reads bounded local labels and references without hashing large image payloads.</summary>
    public async Task<IReadOnlyList<CustomImageReference>> ListAsync(CancellationToken cancellationToken = default)
    {
        string path = OwnedPath("library.json");
        if (!File.Exists(path)) return [];
        await using FileStream input = OpenRead(path);
        if (input.Length > MaximumMetadataBytes) throw new InvalidDataException("The custom image library index is too large.");
        CustomImageReference[] entries = await JsonSerializer.DeserializeAsync<CustomImageReference[]>(input,
            ConfigurationJsonDefaults.SerializerOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The custom image library index is empty.");
        if (entries.Length > CustomImageSettingsValidator.MaximumImages || entries.Any(entry => entry is null || !CustomImageSettingsValidator.IsValidReference(entry)) ||
            entries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count() != entries.Length)
            throw new InvalidDataException("The custom image library index is invalid.");
        return entries;
    }

    /// <summary>Locks the image before verification and retains its handle until disposal.</summary>
    public async Task<CustomImageSourceLease> AcquireAsync(CustomImageReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!CustomImageSettingsValidator.IsValidReference(reference)) throw new InvalidDataException("The custom image reference is invalid.");
        string imagePath = OwnedPath($"content/{reference.ContentHash.ToLowerInvariant()}/image.wim");
        FileStream image = OpenRead(imagePath);
        try
        {
            await VerifyAsync(image, reference.Length, reference.ContentHash, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<CustomImageIndex> indexes = await metadataReader.ReadAsync(imagePath, cancellationToken).ConfigureAwait(false);
            if (!reference.Indexes.Select(index => index.Index).SequenceEqual(indexes.Select(index => index.Index)))
                throw new InvalidDataException("The image indexes do not match their imported metadata.");
            return new CustomImageSourceLease(reference with { Indexes = indexes }, imagePath, image);
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>Deletes only locally owned content, never the original import source or currently leased image.</summary>
    public async Task DeleteAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        ValidateHash(contentHash);
        using FileStream libraryLock = AcquireLibraryLock();
        IReadOnlyList<CustomImageReference> entries = await ListAsync(cancellationToken).ConfigureAwait(false);
        CustomImageReference[] remaining = entries
            .Where(image => !string.Equals(image.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase)).ToArray();
        string imagePath = OwnedPath($"content/{contentHash.ToLowerInvariant()}/image.wim");
        string imageDirectory = Path.GetDirectoryName(imagePath)!;
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(imagePath))
        {
            CustomImagePathPolicy.ValidateNoReparsePoints(imagePath);
            using var deletionLease = new FileStream(imagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete);
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(imagePath);
        }
        if (Directory.Exists(imageDirectory)) Directory.Delete(imageDirectory, recursive: false);
        await WriteIndexAsync(remaining, CancellationToken.None).ConfigureAwait(false);
    }

    private string OwnedPath(string relativePath) => CustomImagePathPolicy.ResolveRelativePath(rootDirectory, relativePath);

    private FileStream AcquireLibraryLock()
    {
        CustomImagePathPolicy.ValidateNoReparsePoints(rootDirectory);
        Directory.CreateDirectory(rootDirectory);
        return new FileStream(OwnedPath(".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static FileStream OpenRead(string path)
    {
        CustomImagePathPolicy.ValidateNoReparsePoints(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    private static byte[] SerializeIndex(IReadOnlyList<CustomImageReference> references)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(references, ConfigurationJsonDefaults.SerializerOptions);
        if (bytes.Length > MaximumMetadataBytes) throw new InvalidDataException("The custom image library index is too large.");
        return bytes;
    }

    private Task WriteIndexAsync(IReadOnlyList<CustomImageReference> references, CancellationToken cancellationToken)
        => WriteIndexAsync(SerializeIndex(references), cancellationToken);

    private async Task WriteIndexAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        string temporary = OwnedPath($"index-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, OwnedPath("library.json"), overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task VerifyAsync(FileStream file, long length, string expectedHash, CancellationToken cancellationToken)
    {
        if (file.Length != length) throw new InvalidDataException("The custom image source size has changed.");
        file.Position = 0;
        string hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
        if (!string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The custom image source content has changed.");
        file.Position = 0;
    }

    private static void ValidateHash(string hash)
    {
        if (!CustomImageSettingsValidator.IsValidHash(hash)) throw new InvalidDataException("The image content hash is invalid.");
    }

    private static void DeleteOwnedDirectory(string path, string allowedRoot)
    {
        string resolved = CustomImagePathPolicy.ResolveRelativePath(allowedRoot, Path.GetRelativePath(allowedRoot, path));
        if (!Directory.Exists(resolved)) return;
        CustomImagePathPolicy.ValidateNoReparsePoints(resolved);
        foreach (string entry in Directory.EnumerateFileSystemEntries(resolved))
        {
            CustomImagePathPolicy.ValidateNoReparsePoints(entry);
            if (Directory.Exists(entry)) DeleteOwnedDirectory(entry, allowedRoot);
            else File.Delete(entry);
        }
        Directory.Delete(resolved, recursive: false);
    }
}
