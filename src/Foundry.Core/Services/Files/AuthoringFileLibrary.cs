// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Packages;

namespace Foundry.Core.Services.Files;

/// <summary>Stores small authoring files by content hash under <c>content/&lt;sha256&gt;/&lt;file name&gt;</c>.</summary>
/// <remarks>
/// Content is immutable and identified by its lowercase hexadecimal SHA-256, so importing the same bytes twice yields one entry.
/// Reads re-verify the hash, because files in the library can be changed outside the application. Imports, leases and
/// <see cref="DeleteUnreferenced"/> are serialized in this process, so a deletion never races an import of the same bytes.
/// </remarks>
public sealed class AuthoringFileLibrary
{
    /// <summary>Largest file the library accepts.</summary>
    public const int MaximumFileBytes = 4 * 1024 * 1024;

    private const int MaximumFileNameLength = 100;
    private readonly string rootDirectory;
    private readonly Lock gate = new();
    private readonly Dictionary<string, int> leases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> importedAt = new(StringComparer.Ordinal);
    private long importSequence;

    /// <summary>Creates a library rooted at <paramref name="rootDirectory"/>, which is created on first import.</summary>
    public AuthoringFileLibrary(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        this.rootDirectory = Path.GetFullPath(rootDirectory);
    }

    /// <summary>Copies a local file into the library, keeping only its sanitized name.</summary>
    /// <exception cref="InvalidDataException">The file is larger than <see cref="MaximumFileBytes"/>.</exception>
    public async Task<AuthoringFileReference> ImportAsync(string sourcePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (input.Length > MaximumFileBytes) throw new InvalidDataException("AuthoringFile.TooLarge");
        byte[] content = new byte[(int)input.Length];
        await input.ReadExactlyAsync(content, ct).ConfigureAwait(false);
        return Import(content, Path.GetFileName(sourcePath));
    }

    /// <summary>Stores <paramref name="content"/> under its hash and a sanitized <paramref name="fileName"/>.</summary>
    /// <exception cref="InvalidDataException">The content is larger than <see cref="MaximumFileBytes"/>.</exception>
    public AuthoringFileReference Import(ReadOnlySpan<byte> content, string fileName)
    {
        if (content.Length > MaximumFileBytes) throw new InvalidDataException("AuthoringFile.TooLarge");
        var reference = new AuthoringFileReference
        {
            ContentHash = Convert.ToHexStringLower(SHA256.HashData(content)),
            FileName = SanitizeFileName(fileName),
            Length = content.Length,
        };
        string path = ContentPath(reference);
        lock (gate)
        {
            importedAt[reference.ContentHash] = ++importSequence;
            if (File.Exists(path) && HasVerifiedContent(path, reference)) return reference;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = Path.Combine(Path.GetDirectoryName(path)!, $".{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllBytes(temporary, content);
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        return reference;
    }

    /// <summary>
    /// Gets a value that increases with every import. Read it together with the references passed to
    /// <see cref="DeleteUnreferenced"/>, so that a file imported after them, whose reference may not be saved yet, is kept.
    /// </summary>
    public long ImportSequence
    {
        get
        {
            lock (gate) return importSequence;
        }
    }

    /// <summary>Keeps the files with <paramref name="contentHashes"/> from <see cref="DeleteUnreferenced"/> until the lease is disposed.</summary>
    /// <remarks>Leases exist in this process only. A media build snapshot holds one while it copies its source files.</remarks>
    public IDisposable Lease(IEnumerable<string> contentHashes)
    {
        ArgumentNullException.ThrowIfNull(contentHashes);
        string[] hashes = contentHashes.Where(hash => !string.IsNullOrWhiteSpace(hash))
            .Select(hash => hash.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray();
        lock (gate)
        {
            foreach (string hash in hashes) leases[hash] = leases.GetValueOrDefault(hash) + 1;
        }
        return new FileLease(this, hashes);
    }

    /// <summary>
    /// Deletes every stored file whose hash is not in <paramref name="referencedHashes"/>, unless it is leased or was imported
    /// after <paramref name="importedBefore"/> was read from <see cref="ImportSequence"/>.
    /// </summary>
    /// <remarks>
    /// A file that cannot be deleted, or whose folder is redirected, is counted as failed and left for a later cleanup. Entries
    /// whose name is not a content hash are not the library's and are never touched.
    /// </remarks>
    /// <exception cref="InvalidDataException">The library folder is redirected (a reparse point).</exception>
    public AuthoringFileCleanupResult DeleteUnreferenced(IReadOnlySet<string> referencedHashes, long importedBefore)
    {
        ArgumentNullException.ThrowIfNull(referencedHashes);
        var referenced = new HashSet<string>(referencedHashes, StringComparer.OrdinalIgnoreCase);
        string contentRoot = Path.Combine(rootDirectory, "content");
        lock (gate)
        {
            if (!Directory.Exists(contentRoot)) return new AuthoringFileCleanupResult(0, 0, 0);
            ContentPackagePathPolicy.ValidateNoReparsePoints(contentRoot);
            int removed = 0, kept = 0, failed = 0;
            foreach (string directory in Directory.EnumerateDirectories(contentRoot))
            {
                string hash = Path.GetFileName(directory);
                if (!IsContentHash(hash)) continue;
                if (referenced.Contains(hash) || leases.ContainsKey(hash) || importedAt.GetValueOrDefault(hash) > importedBefore)
                {
                    kept++;
                    continue;
                }

                try
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("AuthoringFile.RedirectedContent");
                    Directory.Delete(directory, recursive: true);
                    importedAt.Remove(hash);
                    removed++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    failed++;
                }
            }
            return new AuthoringFileCleanupResult(removed, kept, failed);
        }
    }

    /// <summary>Checks that the stored file exists with the referenced length; <see cref="ReadVerified"/> still verifies the hash.</summary>
    public bool IsAvailable(AuthoringFileReference reference)
    {
        try
        {
            string path = ContentPath(reference);
            return File.Exists(path) && new FileInfo(path).Length == reference.Length;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Returns the stored file path when <see cref="IsAvailable"/> holds, otherwise <see langword="null"/>.</summary>
    /// <remarks>Reads through this path are not hash-verified: use it for validation, and <see cref="ReadVerified"/> for media.</remarks>
    public string? GetAvailablePath(AuthoringFileReference reference)
    {
        try
        {
            return IsAvailable(reference) ? ContentPath(reference) : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Reads the stored file and verifies its length and SHA-256 against <paramref name="reference"/>.</summary>
    /// <exception cref="InvalidDataException">The reference is invalid or the content does not match it.</exception>
    public byte[] ReadVerified(AuthoringFileReference reference)
    {
        string path = ContentPath(reference);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length != reference.Length || input.Length > MaximumFileBytes) throw new InvalidDataException("AuthoringFile.ContentMismatch");
        byte[] content = new byte[(int)input.Length];
        input.ReadExactly(content);
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(content)), reference.ContentHash, StringComparison.Ordinal))
            throw new InvalidDataException("AuthoringFile.ContentMismatch");
        return content;
    }

    private static bool HasVerifiedContent(string path, AuthoringFileReference reference)
    {
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return input.Length == reference.Length &&
                string.Equals(Convert.ToHexStringLower(SHA256.HashData(input)), reference.ContentHash, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private void Release(string[] hashes)
    {
        lock (gate)
        {
            foreach (string hash in hashes)
            {
                if (leases.TryGetValue(hash, out int count) && count > 1) leases[hash] = count - 1;
                else leases.Remove(hash);
            }
        }
    }

    private static bool IsContentHash(string? hash) =>
        hash is { Length: 64 } && hash.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private string ContentPath(AuthoringFileReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!IsContentHash(reference.ContentHash))
            throw InvalidReference();
        ValidateFileName(reference.FileName);

        string root = Path.TrimEndingDirectorySeparator(rootDirectory);
        string path = Path.GetFullPath(Path.Combine(root, "content", reference.ContentHash, reference.FileName));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw InvalidReference();
        ContentPackagePathPolicy.ValidateNoReparsePoints(path);
        return path;
    }

    private static void ValidateFileName(string fileName)
    {
        if (fileName is null || fileName.Contains('/')) throw InvalidReference();
        try
        {
            ContentPackagePathPolicy.ValidatePathSyntax(fileName);
        }
        catch (InvalidDataException)
        {
            throw InvalidReference();
        }
    }

    /// <summary>Keeps the last path segment, replaces characters Windows rejects and avoids reserved device names.</summary>
    private static string SanitizeFileName(string? fileName)
    {
        string name = (fileName ?? string.Empty).Split('/', '\\')[^1];
        char[] invalid = Path.GetInvalidFileNameChars();
        name = string.Concat(name.Select(character => Array.IndexOf(invalid, character) >= 0 ? '_' : character)).TrimEnd('.', ' ');
        if (name.Length > MaximumFileNameLength) name = name[..MaximumFileNameLength].TrimEnd('.', ' ');
        if (name.Length == 0) name = "file";
        try
        {
            ContentPackagePathPolicy.ValidatePathSyntax(name);
        }
        catch (InvalidDataException)
        {
            name = "_" + name;
        }
        return name;
    }

    private static InvalidDataException InvalidReference() => new("AuthoringFile.InvalidReference");

    private sealed class FileLease(AuthoringFileLibrary library, string[] hashes) : IDisposable
    {
        private int isReleased;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref isReleased, 1) == 0) library.Release(hashes);
        }
    }
}
