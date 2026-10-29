// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Sharing;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>
/// Reads and writes the signed content index files under <c>&lt;share&gt;\Foundry\Index</c>, one <c>&lt;keyId&gt;.json</c> per
/// signing key. A write goes to a temporary name in the same folder and replaces the final file in one rename, so a reader
/// always sees either the previous or the new whole file, never a partial one and never a missing one. Readers open the file with
/// <see cref="FileShare.Delete"/> shared and take the whole file at once, so they do not block the rename; a holder without
/// delete sharing, or a server that refuses the replace, makes the writer retry it.
/// </summary>
public static class NetworkCacheIndexStore
{
    /// <summary>Folder under <c>Foundry</c> that holds the index files.</summary>
    public const string FolderName = "Index";

    /// <summary>Suffix of the temporary files a write leaves only when interrupted; no reader opens them.</summary>
    public const string TemporarySuffix = ".tmp";

    private const string FileExtension = ".json";
    private const int KeyIdLength = 32;
    private const int ReplaceAttempts = 5;
    private static readonly TimeSpan ReplaceRetryDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Writes <paramref name="signedIndex"/> as <c>&lt;keyId&gt;.json</c>, replacing the previous file. A reader that holds the
    /// destination without delete sharing makes the rename fail, so it is retried 5 times, 200 ms apart.
    /// </summary>
    /// <param name="contentRoot">The <c>&lt;share&gt;\Foundry</c> folder.</param>
    /// <param name="keyId">Fingerprint of the signing key.</param>
    /// <param name="signedIndex">Envelope bytes produced by <see cref="NetworkCacheContentIndexCodec.Write"/>.</param>
    /// <param name="cancellationToken">Cancels the write; the temporary file is deleted.</param>
    /// <exception cref="ArgumentException">The key id is not a key fingerprint, or the file exceeds <see cref="NetworkCacheContentIndexCodec.MaximumBytes"/>.</exception>
    /// <exception cref="IOException">The share rejected the write or the file stayed in use.</exception>
    public static async Task WriteAsync(string contentRoot, string keyId, byte[] signedIndex, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(signedIndex);
        string destination = FilePath(contentRoot, keyId);
        if (signedIndex.Length > NetworkCacheContentIndexCodec.MaximumBytes)
            throw new ArgumentException("The index exceeds the maximum index size.", nameof(signedIndex));

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = $"{destination}.{Guid.NewGuid():N}{TemporarySuffix}";
        try
        {
            await File.WriteAllBytesAsync(temporary, signedIndex, cancellationToken).ConfigureAwait(false);
            await ReplaceAsync(temporary, destination, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    /// <summary>Returns the bytes of <c>&lt;keyId&gt;.json</c>, or <see langword="null"/> when it is missing or larger than the maximum.</summary>
    /// <param name="contentRoot">The <c>&lt;share&gt;\Foundry</c> folder.</param>
    /// <param name="keyId">Fingerprint of the signing key.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="ArgumentException">The key id is not a key fingerprint.</exception>
    /// <exception cref="IOException">The share could not be read.</exception>
    public static Task<byte[]?> ReadAsync(string contentRoot, string keyId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        return ReadFileAsync(FilePath(contentRoot, keyId), cancellationToken);
    }

    /// <summary>
    /// Returns the sequence of the index the trusted key signed, or 0 when there is none or it does not verify, so a publisher
    /// continues from the sequence readers already accepted.
    /// </summary>
    /// <param name="contentRoot">The <c>&lt;share&gt;\Foundry</c> folder.</param>
    /// <param name="trustedPublicKey">SubjectPublicKeyInfo of the key whose index is read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<long> ReadSequenceAsync(string contentRoot, byte[] trustedPublicKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedPublicKey);
        byte[]? file = await ReadAsync(contentRoot, PublicationSigner.KeyId(trustedPublicKey), cancellationToken).ConfigureAwait(false);
        return file is not null
            && NetworkCacheContentIndexCodec.Read(file, trustedPublicKey, 0, out NetworkCacheContentIndex? index) == NetworkCacheIndexReadStatus.Valid
                ? index!.Sequence
                : 0;
    }

    /// <summary>
    /// Returns the payload of every index file on the share without checking signatures, to locate the content any key still
    /// publishes. Malformed, oversized and temporary files are skipped. The result must never drive a deployment.
    /// </summary>
    /// <param name="contentRoot">The <c>&lt;share&gt;\Foundry</c> folder.</param>
    /// <param name="cancellationToken">Cancels between files.</param>
    public static async Task<IReadOnlyList<NetworkCacheContentIndex>> ReadAllUnverifiedAsync(string contentRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        string folder = Path.Combine(contentRoot, FolderName);
        if (!Directory.Exists(folder)) return [];

        List<NetworkCacheContentIndex> indexes = [];
        foreach (string path in Directory.EnumerateFiles(folder, "*" + FileExtension))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)) continue;
            byte[]? file = await ReadFileAsync(path, cancellationToken).ConfigureAwait(false);
            NetworkCacheContentIndex? index = file is null ? null : NetworkCacheContentIndexCodec.ReadUnverified(file);
            if (index is not null) indexes.Add(index);
        }

        return indexes;
    }

    private static string FilePath(string contentRoot, string keyId)
    {
        // The key id becomes a file name: only the fingerprint format is accepted, so it can never leave the folder.
        if (keyId is not { Length: KeyIdLength } || !keyId.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new ArgumentException("The key id is not a key fingerprint.", nameof(keyId));
        return Path.Combine(contentRoot, FolderName, keyId + FileExtension);
    }

    private static async Task ReplaceAsync(string temporary, string destination, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                // MoveFileEx(REPLACE_EXISTING) keeps the name present throughout; File.Replace leaves a window where readers find no file.
                File.Move(temporary, destination, overwrite: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == ReplaceAttempts)
                    throw new IOException("The index file stayed in use and could not be replaced on the share.", exception);
            }

            await Task.Delay(ReplaceRetryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<byte[]?> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > NetworkCacheContentIndexCodec.MaximumBytes) return null;
            byte[] buffer = new byte[(int)stream.Length];
            int read = 0;
            while (read < buffer.Length)
            {
                int count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
                if (count == 0) break;
                read += count;
            }

            return read == buffer.Length ? buffer : buffer[..read];
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
