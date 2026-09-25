// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Images;
using Foundry.Core.Services.Configuration;
using Serilog;

namespace Foundry.Core.Services.Images;

/// <summary>Imports immutable local image content and acquires verified source leases for media builds.</summary>
public sealed partial class CustomImageLibraryService
{
    private const int MaximumMetadataBytes = 8 * 1024 * 1024;
    private const int MaximumSourceFiles = 10000;
    private readonly string rootDirectory;
    private readonly ICustomImageMetadataReader metadataReader;

    public CustomImageLibraryService(string rootDirectory, ICustomImageMetadataReader? metadataReader = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        this.rootDirectory = Path.GetFullPath(rootDirectory);
        this.metadataReader = metadataReader ?? new NativeCustomImageMetadataReader();
    }

    /// <summary>Checks bounded metadata and file sizes for authoring readiness; builds still require verified leases.</summary>
    public bool IsAvailable(CustomImageReference reference)
    {
        if (reference is null || !CustomImageSettingsValidator.IsValidReference(reference)) return false;
        try
        {
            string imagePath = OwnedPath($"content/{reference.ContentHash.ToLowerInvariant()}/image.wim");
            if (!File.Exists(imagePath) || new FileInfo(imagePath).Length != reference.Length) return false;
            if (reference.SourceBundleHash is null) return true;
            string bundle = $"sources/{reference.SourceBundleHash.ToLowerInvariant()}";
            using FileStream manifest = OpenRead(OwnedPath(bundle + "/files.json"));
            if (manifest.Length > MaximumMetadataBytes) return false;
            CustomImageSourceFile[]? files = JsonSerializer.Deserialize<CustomImageSourceFile[]>(manifest, ConfigurationJsonDefaults.SerializerOptions);
            if (files is null) return false;
            ValidateSourceFiles(files);
            if (!string.Equals(ComputeBundleHash(files), reference.SourceBundleHash, StringComparison.OrdinalIgnoreCase)) return false;
            string source = OwnedPath(bundle + "/sxs");
            return files.All(file =>
            {
                string path = CustomImagePathPolicy.ResolveRelativePath(source, file.RelativePath);
                return File.Exists(path) && new FileInfo(path).Length == file.Length;
            });
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
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

    /// <summary>Locks image and companion files before verifying them and retains all handles until disposal.</summary>
    public async Task<CustomImageSourceLease> AcquireAsync(CustomImageReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!CustomImageSettingsValidator.IsValidReference(reference)) throw new InvalidDataException("The custom image reference is invalid.");
        var handles = new List<FileStream>();
        try
        {
            string imagePath = OwnedPath($"content/{reference.ContentHash.ToLowerInvariant()}/image.wim");
            FileStream image = OpenRead(imagePath);
            handles.Add(image);
            string? sourceDirectory = null;
            IReadOnlyList<CustomImageSourceFile> files = [];
            if (reference.SourceBundleHash is not null)
            {
                string bundle = $"sources/{reference.SourceBundleHash.ToLowerInvariant()}";
                FileStream manifest = OpenRead(OwnedPath(bundle + "/files.json"));
                handles.Add(manifest);
                if (manifest.Length > MaximumMetadataBytes) throw new InvalidDataException("The source file manifest is too large.");
                files = await JsonSerializer.DeserializeAsync<CustomImageSourceFile[]>(manifest, ConfigurationJsonDefaults.SerializerOptions, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("The source file manifest is empty.");
                ValidateSourceFiles(files);
                if (!string.Equals(ComputeBundleHash(files), reference.SourceBundleHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The custom image source manifest has changed.");
                sourceDirectory = OwnedPath(bundle + "/sxs");
                foreach (CustomImageSourceFile file in files)
                    handles.Add(OpenRead(CustomImagePathPolicy.ResolveRelativePath(sourceDirectory, file.RelativePath)));
            }
            await VerifyAsync(image, reference.Length, reference.ContentHash, cancellationToken).ConfigureAwait(false);
            int start = reference.SourceBundleHash is null ? 1 : 2;
            for (int i = 0; i < files.Count; i++)
                await VerifyAsync(handles[start + i], files[i].Length, files[i].ContentHash, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<CustomImageIndex> indexes = await metadataReader.ReadAsync(imagePath, cancellationToken).ConfigureAwait(false);
            if (!reference.Indexes.Select(index => index.Index).SequenceEqual(indexes.Select(index => index.Index)))
                throw new InvalidDataException("The image indexes do not match their imported metadata.");
            return new CustomImageSourceLease(reference with { Indexes = indexes }, imagePath, sourceDirectory, files, handles);
        }
        catch
        {
            foreach (FileStream handle in handles) handle.Dispose();
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
        string[] unusedBundles = entries.Except(remaining)
            .Select(image => image.SourceBundleHash).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(hash => !remaining.Any(image => string.Equals(image.SourceBundleHash, hash, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var files = new List<string>();
        var directories = new List<string>();
        string imagePath = OwnedPath($"content/{contentHash.ToLowerInvariant()}/image.wim");
        if (File.Exists(imagePath)) files.Add(imagePath);
        string imageDirectory = Path.GetDirectoryName(imagePath)!;
        if (Directory.Exists(imageDirectory)) directories.Add(imageDirectory);
        foreach (string hash in unusedBundles)
            CollectOwnedDeletionPaths(OwnedPath($"sources/{hash.ToLowerInvariant()}"), files, directories);

        var handles = new List<FileStream>();
        try
        {
            foreach (string path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CustomImagePathPolicy.ValidateNoReparsePoints(path);
                handles.Add(new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete));
            }
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string path in files) File.Delete(path);
        }
        finally
        {
            foreach (FileStream handle in handles) handle.Dispose();
        }
        foreach (string directory in directories.OrderByDescending(path => path.Length))
            Directory.Delete(directory, recursive: false);
        await WriteIndexAsync(remaining, CancellationToken.None).ConfigureAwait(false);
    }

    private void CollectOwnedDeletionPaths(string directory, List<string> files, List<string> directories)
    {
        string owned = OwnedPath(Path.GetRelativePath(rootDirectory, directory));
        if (!Directory.Exists(owned)) return;
        directories.Add(owned);
        foreach (string path in Directory.EnumerateFileSystemEntries(owned))
        {
            CustomImagePathPolicy.ValidateNoReparsePoints(path);
            if (Directory.Exists(path)) CollectOwnedDeletionPaths(path, files, directories);
            else files.Add(path);
        }
    }

    /// <summary>Changes the local library label without modifying immutable content or other profile references.</summary>
    public async Task RenameAsync(string id, string displayName, CancellationToken cancellationToken = default)
    {
        string name = CustomImageSettingsValidator.NormalizeDisplayName(displayName);
        using FileStream libraryLock = AcquireLibraryLock();
        IReadOnlyList<CustomImageReference> entries = await ListAsync(cancellationToken).ConfigureAwait(false);
        if (!entries.Any(entry => entry.Id == id)) throw new InvalidDataException("The custom image is unavailable.");
        await WriteIndexAsync(entries.Select(entry => entry.Id == id ? entry with { DisplayName = name } : entry).ToArray(), cancellationToken).ConfigureAwait(false);
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

    private async Task WriteIndexAsync(IReadOnlyList<CustomImageReference> references, CancellationToken cancellationToken)
    {
        string temporary = OwnedPath($"index-{Guid.NewGuid():N}.tmp");
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(references, ConfigurationJsonDefaults.SerializerOptions);
            if (bytes.Length > MaximumMetadataBytes) throw new InvalidDataException("The custom image library index is too large.");
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
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

    private static void ValidateSourceFiles(IReadOnlyList<CustomImageSourceFile> files)
    {
        if (files.Count > MaximumSourceFiles || files.Any(file => file is null || file.Length < 0 ||
            !CustomImageSettingsValidator.IsValidHash(file.ContentHash)) ||
            files.Select(file => file.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
            throw new InvalidDataException("The optional-feature source manifest is invalid.");
    }

    private static string ComputeBundleHash(IReadOnlyList<CustomImageSourceFile> files)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray(),
            ConfigurationJsonDefaults.SerializerOptions);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
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
