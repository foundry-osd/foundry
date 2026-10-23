// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Foundry.Core.Services.Sharing;

/// <summary>
/// Encrypts and decrypts publication content files (<c>content/&lt;sha256&gt;.bin</c>, spec 5.5) with chunked AES-256-GCM.
/// </summary>
/// <remarks>
/// File layout: a 16-byte header (ASCII magic <c>FCNT</c>, int32 little-endian version 1, int64 little-endian plaintext length),
/// then one record per chunk: 12-byte random nonce, 16-byte tag, ciphertext. Every chunk holds <see cref="ChunkSize"/> bytes except
/// the last; empty content has one empty chunk. The associated data of a chunk is the header, the int64 little-endian chunk index
/// and a final-chunk flag byte, so reordered, dropped, truncated or appended chunks fail authentication. The layout is an on-disk
/// contract shared by every Foundry version that reads the folder.
/// </remarks>
public static class ContentTransfer
{
    /// <summary>Plaintext bytes per chunk.</summary>
    public const int ChunkSize = 1024 * 1024;

    private const int Version = 1;
    private const int HeaderSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int ChunkOverhead = NonceSize + TagSize;
    private const int FileBufferSize = 128 * 1024;
    private const string KeyInfoPrefix = "foundry-content/";
    private static ReadOnlySpan<byte> Magic => "FCNT"u8;

    /// <summary>
    /// Derives the AES-256 key of one content item: HKDF-SHA256 over <paramref name="masterKey"/> with info
    /// <c>"foundry-content/" + hash</c> (UTF-8) and an empty salt. The master key is 32 uniformly random bytes, so the
    /// extract step needs no salt (RFC 5869, section 3.1); the info binds each key to its content hash.
    /// </summary>
    /// <param name="masterKey">The publication master key (32 bytes).</param>
    /// <param name="hash">Lowercase SHA-256 content hash.</param>
    /// <returns>A new 32-byte key; the caller owns it.</returns>
    /// <exception cref="ArgumentException">The master key is not 32 bytes or the hash is not 64 lowercase hex digits.</exception>
    public static byte[] DeriveContentKey(ReadOnlySpan<byte> masterKey, string hash)
    {
        if (masterKey.Length != KeySize)
        {
            throw new ArgumentException("The publication master key must be 32 bytes.", nameof(masterKey));
        }

        if (!IsContentHash(hash))
        {
            throw new ArgumentException("The content hash must be 64 lowercase hexadecimal digits.", nameof(hash));
        }

        byte[] key = new byte[KeySize];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, key, salt: [], Encoding.UTF8.GetBytes(KeyInfoPrefix + hash));
        return key;
    }

    /// <summary>
    /// Encrypts the rest of <paramref name="source"/> into <paramref name="destinationPath"/>. The file is written as
    /// <c>&lt;destination&gt;.tmp</c> and renamed over the destination only when complete; on failure or cancellation the temporary
    /// file is removed and an existing destination is left unchanged.
    /// </summary>
    /// <param name="source">Seekable stream; its remaining length is fixed in the header before the first chunk is written.</param>
    /// <param name="destinationPath">Content file to create or replace.</param>
    /// <param name="key">Key from <see cref="DeriveContentKey"/>.</param>
    /// <param name="progress">Receives the plaintext bytes encrypted so far, once per chunk.</param>
    /// <param name="cancellationToken">Stops between chunks.</param>
    /// <exception cref="ArgumentException">The source is not seekable or the key is not 32 bytes.</exception>
    /// <exception cref="InvalidDataException">The source length changed while it was read.</exception>
    public static async Task EncryptAsync(Stream source, string destinationPath, ReadOnlyMemory<byte> key, IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ValidateKey(key);
        if (!source.CanSeek)
        {
            throw new ArgumentException("The content source must be seekable.", nameof(source));
        }

        long length = source.Length - source.Position;
        byte[] header = CreateHeader(length);
        long chunkCount = ChunkCount(length);
        string temporaryPath = destinationPath + ".tmp";
        try
        {
            await using (FileStream output = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, FileBufferSize, FileOptions.Asynchronous))
            {
                await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
                await EncryptChunksAsync(source, output, header, chunkCount, length, key, progress, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        catch
        {
            PublicationFolder.DeleteBestEffort(temporaryPath);
            throw;
        }
    }

    /// <summary>
    /// Decrypts <paramref name="sourcePath"/> into <paramref name="destination"/> and returns the lowercase SHA-256 of the plaintext.
    /// Each chunk is authenticated before it is written, but a later chunk can still fail: on any exception the caller must discard
    /// what was written to <paramref name="destination"/>. The returned hash is the content hash only for <see cref="SharedContentKind.File"/>
    /// content, which the caller compares with it. For <see cref="SharedContentKind.Package"/> and <see cref="SharedContentKind.DriverSet"/>
    /// the plaintext is a package archive, whose content hash (that of its canonical manifest) is checked by
    /// <see cref="ContentPackageArchive.ImportAsync"/> against the listed hash.
    /// </summary>
    /// <param name="sourcePath">Content file written by <see cref="EncryptAsync"/>.</param>
    /// <param name="destination">Receives the plaintext.</param>
    /// <param name="key">Key from <see cref="DeriveContentKey"/>.</param>
    /// <param name="progress">Receives the plaintext bytes decrypted so far, once per chunk.</param>
    /// <param name="cancellationToken">Stops between chunks.</param>
    /// <exception cref="InvalidDataException">The file is malformed, truncated, extended, or fails authentication (wrong key or modified).</exception>
    public static async Task<string> DecryptAsync(string sourcePath, Stream destination, ReadOnlyMemory<byte> key, IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(destination);
        ValidateKey(key);
        await using FileStream input = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] header = new byte[HeaderSize];
        if (input.Length < HeaderSize)
        {
            throw InvalidContent();
        }

        await input.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        long length = ReadHeaderLength(header);
        if (length < 0 || length > input.Length || input.Length != EncryptedLength(length))
        {
            throw InvalidContent();
        }

        long chunkCount = ChunkCount(length);
        byte[] record = new byte[ChunkOverhead + ChunkSize];
        byte[] plaintext = new byte[ChunkSize];
        using IncrementalHash sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using AesGcm aes = new(key.Span, TagSize);
        long done = 0;
        for (long index = 0; index < chunkCount; index++)
        {
            int size = ChunkLength(length, index);
            await input.ReadExactlyAsync(record.AsMemory(0, ChunkOverhead + size), cancellationToken).ConfigureAwait(false);
            try
            {
                aes.Decrypt(record.AsSpan(0, NonceSize), record.AsSpan(ChunkOverhead, size), record.AsSpan(NonceSize, TagSize),
                    plaintext.AsSpan(0, size), CreateAssociatedData(header, index, index == chunkCount - 1));
            }
            catch (AuthenticationTagMismatchException exception)
            {
                throw new InvalidDataException("ContentTransfer.ContentAuthenticationFailed", exception);
            }

            sha256.AppendData(plaintext, 0, size);
            await destination.WriteAsync(plaintext.AsMemory(0, size), cancellationToken).ConfigureAwait(false);
            done += size;
            progress?.Report(done);
        }

        return Convert.ToHexStringLower(sha256.GetHashAndReset());
    }

    /// <summary>
    /// Returns whether <paramref name="path"/> is a complete content file written under <paramref name="key"/>: a well-formed header
    /// whose plaintext length matches the file size, and a final chunk that authenticates with the key, the header, its index and the
    /// final flag. This binds a reused file to the current master key (a file left by another master key, after a reset or a take
    /// over, or a planted file of the right size, is rewritten). Earlier chunks are not authenticated here; readers check them.
    /// </summary>
    internal static bool IsComplete(string path, ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySize)
        {
            throw new ArgumentException("The content key must be 32 bytes.", nameof(key));
        }

        try
        {
            using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] header = new byte[HeaderSize];
            if (input.Length < HeaderSize)
            {
                return false;
            }

            input.ReadExactly(header);
            long length = ReadHeaderLength(header);
            if (length < 0 || length > input.Length || input.Length != EncryptedLength(length))
            {
                return false;
            }

            long last = ChunkCount(length) - 1;
            int size = ChunkLength(length, last);
            byte[] record = new byte[ChunkOverhead + size];
            input.Position = input.Length - record.Length;
            input.ReadExactly(record);
            using AesGcm aes = new(key, TagSize);
            aes.Decrypt(record.AsSpan(0, NonceSize), record.AsSpan(ChunkOverhead, size), record.AsSpan(NonceSize, TagSize),
                new byte[size], CreateAssociatedData(header, last, final: true));
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or AuthenticationTagMismatchException)
        {
            return false;
        }
    }

    /// <summary>Returns the content file size for a plaintext of <paramref name="length"/> bytes.</summary>
    private static long EncryptedLength(long length) => checked(HeaderSize + (ChunkCount(length) * ChunkOverhead) + length);

    private static async Task EncryptChunksAsync(Stream source, FileStream output, byte[] header, long chunkCount, long length,
        ReadOnlyMemory<byte> key, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        byte[] plaintext = new byte[ChunkSize];
        byte[] record = new byte[ChunkOverhead + ChunkSize];
        using AesGcm aes = new(key.Span, TagSize);
        long done = 0;
        for (long index = 0; index < chunkCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int size = ChunkLength(length, index);
            int read = await source.ReadAtLeastAsync(plaintext.AsMemory(0, size), size, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            if (read != size)
            {
                throw new InvalidDataException("ContentTransfer.SourceChanged");
            }

            RandomNumberGenerator.Fill(record.AsSpan(0, NonceSize));
            aes.Encrypt(record.AsSpan(0, NonceSize), plaintext.AsSpan(0, size), record.AsSpan(ChunkOverhead, size), record.AsSpan(NonceSize, TagSize),
                CreateAssociatedData(header, index, index == chunkCount - 1));
            await output.WriteAsync(record.AsMemory(0, ChunkOverhead + size), cancellationToken).ConfigureAwait(false);
            done += size;
            progress?.Report(done);
        }

        if (await source.ReadAsync(plaintext.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) != 0)
        {
            throw new InvalidDataException("ContentTransfer.SourceChanged");
        }
    }

    private static byte[] CreateHeader(long length)
    {
        byte[] header = new byte[HeaderSize];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), Version);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(8), length);
        return header;
    }

    /// <summary>Returns the plaintext length of a header, or throws when the magic or version is not this format.</summary>
    private static long ReadHeaderLength(ReadOnlySpan<byte> header)
    {
        if (!header[..4].SequenceEqual(Magic) || BinaryPrimitives.ReadInt32LittleEndian(header[4..]) != Version)
        {
            throw InvalidContent();
        }

        return BinaryPrimitives.ReadInt64LittleEndian(header[8..]);
    }

    private static byte[] CreateAssociatedData(ReadOnlySpan<byte> header, long index, bool final)
    {
        byte[] associatedData = new byte[HeaderSize + sizeof(long) + 1];
        header.CopyTo(associatedData);
        BinaryPrimitives.WriteInt64LittleEndian(associatedData.AsSpan(HeaderSize), index);
        associatedData[^1] = final ? (byte)1 : (byte)0;
        return associatedData;
    }

    private static long ChunkCount(long length) => length == 0 ? 1 : ((length - 1) / ChunkSize) + 1;

    private static int ChunkLength(long length, long index) => (int)Math.Min(ChunkSize, length - (index * ChunkSize));

    private static void ValidateKey(ReadOnlyMemory<byte> key)
    {
        if (key.Length != KeySize)
        {
            throw new ArgumentException("The content key must be 32 bytes.", nameof(key));
        }
    }

    /// <summary>Returns whether <paramref name="hash"/> is a content hash as publications list it: 64 lowercase hexadecimal digits.</summary>
    internal static bool IsContentHash(string? hash) => hash is { Length: 64 } && hash.All(char.IsAsciiHexDigitLower);

    private static InvalidDataException InvalidContent() => new("ContentTransfer.InvalidContentFile");
}
