// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Utilities.IO;

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Stores WinRE images extracted from Windows source packages, one verified entry per architecture and language.
/// </summary>
/// <remarks>
/// Entries are named <c>&lt;arch&gt;-&lt;language&gt;-&lt;source hash prefix&gt;</c>. They are built in a pending folder,
/// receive their manifest last and are then renamed, so a folder with a manifest is complete. Every reuse rehashes the
/// files; an entry that fails verification is deleted so the next build extracts it again.
/// </remarks>
internal sealed class WinReCache(string rootPath)
{
    private const string ManifestFileName = "manifest.json";
    private const string PendingPrefix = ".pending-";
    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };

    private readonly string _rootPath = Path.GetFullPath(rootPath);

    /// <summary>
    /// Serializes cache use for one architecture and language across Foundry processes until the lock is disposed.
    /// </summary>
    public async Task<IAsyncDisposable> AcquireLockAsync(WinPeArchitecture architecture, string language, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_rootPath);
        string lockPath = Path.Combine(_rootPath, $"{GetEntryPrefix(architecture, language)}.lock");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception) when (IsSharingViolation(exception))
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Returns the newest verified entry for the architecture and language, restricted to one source hash when given.
    /// Only matching entries are hashed; an entry that fails verification is deleted best-effort, while one that cannot
    /// be read because another process holds it is kept for a later build.
    /// </summary>
    public async Task<WinReCacheEntry?> FindValidAsync(
        WinPeArchitecture architecture,
        string language,
        string? sourceSha256,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_rootPath))
        {
            return null;
        }

        var candidates = new List<WinReCacheEntry>();
        foreach (string directory in EnumerateEntries(GetEntryPrefix(architecture, language)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ManifestReadResult read = await ReadManifestAsync(directory, architecture, language, cancellationToken).ConfigureAwait(false);
            if (read.Manifest is null)
            {
                if (!read.IsLocked) TryDeleteDirectory(directory);
                continue;
            }

            if (sourceSha256 is null || read.Manifest.SourceSha256.Equals(sourceSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(new WinReCacheEntry(directory, read.Manifest));
            }
        }

        foreach (WinReCacheEntry candidate in candidates.OrderByDescending(entry => entry.Manifest.BuildMajor).ThenByDescending(entry => entry.Manifest.BuildUbr))
        {
            FileCheck check = await VerifyFilesAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (check == FileCheck.Valid)
            {
                return candidate;
            }

            if (check == FileCheck.Corrupted)
            {
                TryDeleteDirectory(candidate.DirectoryPath);
            }
        }

        return null;
    }

    /// <summary>
    /// Creates an empty folder in which a new entry is assembled before <see cref="PublishAsync"/>. Called under the
    /// architecture and language lock, it first removes pending folders of the same pair left by an interrupted build.
    /// </summary>
    public string CreatePendingDirectory(WinPeArchitecture architecture, string language)
    {
        Directory.CreateDirectory(_rootPath);
        string pendingPrefix = $"{PendingPrefix}{GetEntryPrefix(architecture, language)}-";
        foreach (string stale in Directory.EnumerateDirectories(_rootPath, pendingPrefix + "*"))
        {
            string suffix = Path.GetFileName(stale)[pendingPrefix.Length..];
            if (suffix.Length == 32 && suffix.All(Uri.IsHexDigit))
            {
                TryDeleteDirectory(stale);
            }
        }

        string path = Path.Combine(_rootPath, pendingPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Hashes the listed files, writes the manifest last, publishes the pending folder as the entry of its source and
    /// removes other entries of the same architecture and language best-effort.
    /// </summary>
    public async Task<WinReCacheEntry> PublishAsync(
        string pendingDirectory,
        WinReCacheSource source,
        IReadOnlyList<string> fileNames,
        CancellationToken cancellationToken)
    {
        var files = new List<WinReCacheFile>(fileNames.Count);
        foreach (string fileName in fileNames)
        {
            string path = Path.Combine(pendingDirectory, fileName);
            string sha256 = await FileHash.ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
            files.Add(new WinReCacheFile(fileName, new FileInfo(path).Length, sha256));
        }

        var manifest = new WinReCacheManifest
        {
            Architecture = FormatArchitecture(source.Architecture),
            Language = WinPeLanguageUtility.Normalize(source.Language),
            BuildMajor = source.BuildMajor,
            BuildUbr = source.BuildUbr,
            Edition = source.Edition,
            SourceFileName = source.SourceFileName,
            SourceSha256 = source.SourceSha256.Trim().ToUpperInvariant(),
            Files = files
        };

        string manifestPath = Path.Combine(pendingDirectory, ManifestFileName);
        await using (var stream = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, manifest, ManifestJsonOptions, cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        // A source without catalog hash is keyed by its file name so the entry stays addressable.
        string identity = manifest.SourceSha256.Length > 0
            ? manifest.SourceSha256
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.SourceFileName)));
        string prefix = GetEntryPrefix(source.Architecture, source.Language);
        string entryPath = Path.Combine(_rootPath, $"{prefix}-{identity[..Math.Min(16, identity.Length)]}");
        TryDeleteDirectory(entryPath);
        Directory.Move(pendingDirectory, entryPath);

        foreach (string directory in EnumerateEntries(prefix))
        {
            if (!directory.Equals(entryPath, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteDirectory(directory);
            }
        }

        return new WinReCacheEntry(entryPath, manifest);
    }

    private static async Task<ManifestReadResult> ReadManifestAsync(
        string directory,
        WinPeArchitecture architecture,
        string language,
        CancellationToken cancellationToken)
    {
        WinReCacheManifest? manifest;
        try
        {
            await using FileStream stream = File.OpenRead(Path.Combine(directory, ManifestFileName));
            manifest = await JsonSerializer.DeserializeAsync<WinReCacheManifest>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (IOException exception) when (IsSharingViolation(exception))
        {
            return new ManifestReadResult(null, IsLocked: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new ManifestReadResult(null, IsLocked: false);
        }

        bool isValid = manifest is { Version: 1, Files.Count: > 0 } &&
            manifest.Files.All(file => file.Name == Path.GetFileName(file.Name)) &&
            manifest.Architecture.Equals(FormatArchitecture(architecture), StringComparison.OrdinalIgnoreCase) &&
            manifest.Language.Equals(WinPeLanguageUtility.Normalize(language), StringComparison.OrdinalIgnoreCase);
        return new ManifestReadResult(isValid ? manifest : null, IsLocked: false);
    }

    private static async Task<FileCheck> VerifyFilesAsync(WinReCacheEntry entry, CancellationToken cancellationToken)
    {
        foreach (WinReCacheFile file in entry.Manifest.Files)
        {
            string path = entry.GetFilePath(file.Name);
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length != file.Length)
                {
                    return FileCheck.Corrupted;
                }

                string actual = await FileHash.ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
                if (!actual.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return FileCheck.Corrupted;
                }
            }
            catch (IOException exception) when (IsSharingViolation(exception))
            {
                return FileCheck.Locked;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return FileCheck.Corrupted;
            }
        }

        return FileCheck.Valid;
    }

    private static bool IsSharingViolation(IOException exception) => (exception.HResult & 0xFFFF) is 32 or 33;
    /// <summary>
    /// Lists entry folders of exactly one architecture and language; a bare wildcard would also match longer language
    /// tags such as <c>sr-latn</c> and <c>sr-latn-rs</c>.
    /// </summary>
    private IEnumerable<string> EnumerateEntries(string prefix) =>
        Directory.EnumerateDirectories(_rootPath, prefix + "-*").Where(directory =>
        {
            string suffix = Path.GetFileName(directory)[(prefix.Length + 1)..];
            return suffix.Length is > 0 and <= 16 && suffix.All(Uri.IsHexDigit);
        });

    private static string GetEntryPrefix(WinPeArchitecture architecture, string language) =>
        $"{FormatArchitecture(architecture)}-{PathSegment.Sanitize(WinPeLanguageUtility.Normalize(language))}";

    private static string FormatArchitecture(WinPeArchitecture architecture) => architecture switch
    {
        WinPeArchitecture.Arm64 => "arm64",
        _ => "x64"
    };

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort: an entry in use by another process is retried on a later build.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort, as above.
        }
    }

    private sealed record ManifestReadResult(WinReCacheManifest? Manifest, bool IsLocked);

    private enum FileCheck
    {
        Valid,
        Corrupted,
        Locked
    }
}
