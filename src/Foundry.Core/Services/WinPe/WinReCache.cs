// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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
            catch (IOException exception) when ((exception.HResult & 0xFFFF) is 32 or 33)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Returns the newest verified entry for the architecture and language, restricted to one source hash when given.
    /// Entries that fail verification are deleted best-effort.
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

        string prefix = GetEntryPrefix(architecture, language);
        WinReCacheEntry? newest = null;
        foreach (string directory in EnumerateEntries(prefix))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WinReCacheEntry? entry = await VerifyAsync(directory, architecture, language, cancellationToken).ConfigureAwait(false);
            if (entry is null)
            {
                TryDeleteDirectory(directory);
                continue;
            }

            if (sourceSha256 is not null && !entry.Manifest.SourceSha256.Equals(sourceSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (newest is null || CompareBuild(entry.Manifest, newest.Manifest) > 0)
            {
                newest = entry;
            }
        }

        return newest;
    }

    /// <summary>
    /// Creates an empty folder in which a new entry is assembled before <see cref="PublishAsync"/>.
    /// </summary>
    public string CreatePendingDirectory()
    {
        string path = Path.Combine(_rootPath, PendingPrefix + Guid.NewGuid().ToString("N"));
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

        string prefix = GetEntryPrefix(source.Architecture, source.Language);
        string entryPath = Path.Combine(_rootPath, $"{prefix}-{manifest.SourceSha256[..Math.Min(16, manifest.SourceSha256.Length)]}");
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

    private static async Task<WinReCacheEntry?> VerifyAsync(
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }

        if (manifest is null || manifest.Version != 1 || manifest.Files is not { Count: > 0 } ||
            !manifest.Architecture.Equals(FormatArchitecture(architecture), StringComparison.OrdinalIgnoreCase) ||
            !manifest.Language.Equals(WinPeLanguageUtility.Normalize(language), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (WinReCacheFile file in manifest.Files)
        {
            string path = Path.Combine(directory, file.Name);
            if (file.Name != Path.GetFileName(file.Name) || !File.Exists(path) || new FileInfo(path).Length != file.Length)
            {
                return null;
            }

            string actual = await FileHash.ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
            if (!actual.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return new WinReCacheEntry(directory, manifest);
    }

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

    private static int CompareBuild(WinReCacheManifest left, WinReCacheManifest right)
    {
        int build = left.BuildMajor.CompareTo(right.BuildMajor);
        return build != 0 ? build : left.BuildUbr.CompareTo(right.BuildUbr);
    }

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
}
