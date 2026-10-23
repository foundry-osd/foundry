// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Text.Json;

namespace Foundry.Core.Services.Sharing;

/// <summary>Content of <c>current.json</c>: names the active manifest of a publication folder.</summary>
/// <param name="Format">Always <c>foundry-publication</c>.</param>
/// <param name="Version">Pointer format version.</param>
/// <param name="Manifest">Folder-relative manifest path with forward slashes, <c>manifests/&lt;sequence&gt;-&lt;id&gt;.fcfg</c>.</param>
internal sealed record PublicationPointer(string Format, int Version, string Manifest);

/// <summary>How <c>current.json</c> was found.</summary>
internal enum PublicationPointerState
{
    /// <summary>The pointer is well-formed and names a manifest.</summary>
    Ok,

    /// <summary>The folder has no <c>current.json</c>.</summary>
    Missing,

    /// <summary>The pointer is not a publication pointer or names an invalid manifest path.</summary>
    Invalid,

    /// <summary>The pointer version is newer than this Foundry version reads.</summary>
    Unsupported
}

/// <summary>Result of reading <c>current.json</c>.</summary>
/// <param name="State">How the pointer was found.</param>
/// <param name="Manifest">Manifest path from the pointer; set only when <paramref name="State"/> is <see cref="PublicationPointerState.Ok"/>.</param>
/// <param name="Sequence">Sequence parsed from the manifest file name; 0 unless <paramref name="State"/> is <see cref="PublicationPointerState.Ok"/>.</param>
internal sealed record PublicationPointerRead(PublicationPointerState State, string? Manifest, long Sequence);

/// <summary>
/// Layout of a publication folder (spec 5.5): <c>current.json</c>, <c>manifests/&lt;sequence&gt;-&lt;id&gt;.fcfg</c> and
/// <c>content/&lt;sha256&gt;.bin</c>. Manifest names are the sequence zero-padded to at least 6 digits, a dash, a new
/// <see cref="Guid"/> in "N" format (32 lowercase hex digits) and <c>.fcfg</c>; readers take the sequence from the name so they can
/// skip an unchanged publication without opening its manifest. Every file is written to <c>&lt;name&gt;.tmp</c> and renamed.
/// </summary>
internal static class PublicationFolder
{
    /// <summary>Pointer <c>format</c> value.</summary>
    internal const string Format = "foundry-publication";

    /// <summary>Pointer version written by this Foundry version; readers accept 1 to this value.</summary>
    internal const int CurrentVersion = 1;

    private const string PointerFileName = "current.json";
    private const string ManifestsDirectoryName = "manifests";
    private const string ContentDirectoryName = "content";
    private const string ManifestExtension = ".fcfg";
    private const string ContentExtension = ".bin";
    private const string TemporaryExtension = ".tmp";
    private const int MaximumPointerBytes = 4096;
    private const int MinimumSequenceDigits = 6;
    private const int MaximumSequenceDigits = 18;
    private const int ManifestIdLength = 32;

    /// <summary>Highest sequence a manifest name can carry (18 digits).</summary>
    internal const long MaximumSequence = 999_999_999_999_999_999;

    /// <summary>Returns the path of <c>current.json</c>.</summary>
    internal static string PointerPath(string folder) => Path.Combine(folder, PointerFileName);

    /// <summary>Returns the temporary file <see cref="WritePointerAsync"/> renames over <c>current.json</c>.</summary>
    internal static string PointerTemporaryPath(string folder) => PointerPath(folder) + TemporaryExtension;

    /// <summary>Returns the <c>content</c> directory.</summary>
    internal static string ContentDirectory(string folder) => Path.Combine(folder, ContentDirectoryName);

    /// <summary>Returns the <c>manifests</c> directory.</summary>
    internal static string ManifestsDirectory(string folder) => Path.Combine(folder, ManifestsDirectoryName);

    /// <summary>Returns the content file of a lowercase content hash.</summary>
    internal static string ContentPath(string folder, string hash) => Path.Combine(ContentDirectory(folder), hash + ContentExtension);

    /// <summary>Returns the local path of a pointer manifest path (<c>manifests/&lt;file&gt;</c>).</summary>
    internal static string ManifestPath(string folder, string manifest) => Path.Combine(ManifestsDirectory(folder), ManifestFileName(manifest));

    /// <summary>Returns a new pointer manifest path for <paramref name="sequence"/>.</summary>
    internal static string NewManifest(long sequence) =>
        string.Create(CultureInfo.InvariantCulture, $"{ManifestsDirectoryName}/{sequence:D6}-{Guid.NewGuid():N}{ManifestExtension}");

    /// <summary>Returns the pointer manifest path (<c>manifests/&lt;file&gt;</c>) of a manifest file name.</summary>
    internal static string ManifestPointerPath(string fileName) => ManifestsDirectoryName + "/" + fileName;

    /// <summary>Returns the file name part of a pointer manifest path.</summary>
    internal static string ManifestFileName(string manifest) => manifest[(ManifestsDirectoryName.Length + 1)..];

    /// <summary>
    /// Reads <c>current.json</c>. Missing or malformed files are reported through the state; file system failures other than a
    /// missing pointer propagate as <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    internal static async Task<PublicationPointerRead> ReadPointerAsync(string folder, CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            await using FileStream stream = new(PointerPath(folder), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, useAsync: true);
            if (stream.Length > MaximumPointerBytes)
            {
                return new(PublicationPointerState.Invalid, null, 0);
            }

            bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return new(PublicationPointerState.Missing, null, 0);
        }

        return ParsePointer(bytes);
    }

    /// <summary>Activates <paramref name="manifest"/> by replacing <c>current.json</c> atomically.</summary>
    internal static Task WritePointerAsync(string folder, string manifest, CancellationToken cancellationToken)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new PublicationPointer(Format, CurrentVersion, manifest), ConfigurationPackageJson.SerializerOptions);
        return WriteAtomicAsync(PointerPath(folder), bytes, cancellationToken);
    }

    /// <summary>Writes <paramref name="bytes"/> to <c>&lt;path&gt;.tmp</c>, flushes it to disk and renames it over <paramref name="path"/>.</summary>
    internal static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        string temporaryPath = path + TemporaryExtension;
        try
        {
            await using (FileStream stream = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            DeleteBestEffort(temporaryPath);
            throw;
        }
    }

    /// <summary>
    /// Returns whether a folder holds nothing but files a publication writes (<c>current.json</c>, content, manifests and their
    /// <c>.tmp</c> leftovers). The writer starts a new publication in such a folder when none of its manifests authenticates.
    /// </summary>
    internal static bool HoldsOnlyPublicationFiles(string folder)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
        {
            string name = Path.GetFileName(entry);
            bool owned = Directory.Exists(entry)
                ? name switch
                {
                    ContentDirectoryName => HoldsOnlyFiles(entry, fileName => ContentHashOf(fileName) is not null),
                    ManifestsDirectoryName => HoldsOnlyFiles(entry, fileName => TryParseManifestFileName(fileName, out _)),
                    _ => false
                }
                : name is PointerFileName or PointerFileName + TemporaryExtension;
            if (!owned)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Returns <paramref name="fileName"/> without its <c>.tmp</c> extension, or <see langword="null"/> when it has none.</summary>
    internal static string? WithoutTemporaryExtension(string fileName) =>
        fileName.EndsWith(TemporaryExtension, StringComparison.Ordinal) ? fileName[..^TemporaryExtension.Length] : null;

    /// <summary>Deletes a file without throwing, for cleanup that must not replace a propagating exception or fail a finished write.</summary>
    internal static void DeleteBestEffort(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left for the next publish, whose cleanup removes leftovers.
        }
    }

    /// <summary>Returns the lowercase content hash of a content file name, or <see langword="null"/> when it is not one.</summary>
    internal static string? ContentHashOf(string fileName)
    {
        string hash = Path.GetFileNameWithoutExtension(fileName);
        return Path.GetExtension(fileName) == ContentExtension && ContentTransfer.IsContentHash(hash) ? hash : null;
    }

    private static PublicationPointerRead ParsePointer(byte[] bytes)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = ConfigurationPackageJson.MaxDepth });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("format", out JsonElement format) || format.ValueKind != JsonValueKind.String || !format.ValueEquals(Format)
                || !root.TryGetProperty("version", out JsonElement version) || !version.TryGetInt32(out int versionNumber) || versionNumber < 1)
            {
                return new(PublicationPointerState.Invalid, null, 0);
            }

            if (versionNumber > CurrentVersion)
            {
                return new(PublicationPointerState.Unsupported, null, 0);
            }

            return root.TryGetProperty("manifest", out JsonElement manifest)
                && manifest.ValueKind == JsonValueKind.String
                && manifest.GetString() is { } path
                && path.StartsWith(ManifestsDirectoryName + "/", StringComparison.Ordinal)
                && TryParseManifestFileName(ManifestFileName(path), out long sequence)
                    ? new(PublicationPointerState.Ok, path, sequence)
                    : new(PublicationPointerState.Invalid, null, 0);
        }
        catch (JsonException)
        {
            return new(PublicationPointerState.Invalid, null, 0);
        }
    }

    /// <summary>Parses a manifest file name, <c>&lt;digits&gt;-&lt;32 lowercase hex&gt;.fcfg</c>; the sequence must be at least 1.</summary>
    internal static bool TryParseManifestFileName(string fileName, out long sequence)
    {
        sequence = 0;
        int dash = fileName.IndexOf('-', StringComparison.Ordinal);
        if (dash is < MinimumSequenceDigits or > MaximumSequenceDigits
            || fileName.Length != dash + 1 + ManifestIdLength + ManifestExtension.Length
            || !fileName.EndsWith(ManifestExtension, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> digits = fileName.AsSpan(0, dash);
        ReadOnlySpan<char> id = fileName.AsSpan(dash + 1, ManifestIdLength);
        return !digits.ContainsAnyExceptInRange('0', '9')
            && !id.ContainsAnyExcept("0123456789abcdef")
            && long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out sequence)
            && sequence >= 1;
    }

    /// <summary>Returns whether <paramref name="directory"/> has no subdirectory and only owned files or their <c>.tmp</c> leftovers.</summary>
    private static bool HoldsOnlyFiles(string directory, Func<string, bool> isOwnedFileName)
    {
        return !Directory.EnumerateDirectories(directory).Any()
            && Directory.EnumerateFiles(directory).All(path =>
            {
                string name = Path.GetFileName(path);
                return isOwnedFileName(WithoutTemporaryExtension(name) ?? name);
            });
    }
}
