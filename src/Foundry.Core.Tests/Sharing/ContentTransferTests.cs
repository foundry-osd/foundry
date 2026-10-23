// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Services.Sharing;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Sharing;

public sealed class ContentTransferTests : IDisposable
{
    private const string Hash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";
    private const int HeaderBytes = 16;
    private const int ChunkOverheadBytes = 12 + 16;

    private readonly TemporaryDirectory directory = new();
    private readonly byte[] key = ContentTransfer.DeriveContentKey(RandomNumberGenerator.GetBytes(32), Hash);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EncryptDecrypt_MultiChunk_RoundTripsAndReturnsHash()
    {
        byte[] plaintext = RandomNumberGenerator.GetBytes((ContentTransfer.ChunkSize * 5) / 2);
        string path = await EncryptAsync(plaintext);
        List<long> reports = [];

        using MemoryStream decrypted = new();
        string hash = await ContentTransfer.DecryptAsync(path, decrypted, key, new InlineProgress(reports.Add), Cancellation);

        Assert.Equal(plaintext, decrypted.ToArray());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(plaintext)), hash);
        Assert.Equal(HeaderBytes + (3 * ChunkOverheadBytes) + plaintext.Length, new FileInfo(path).Length);
        Assert.Equal(plaintext.Length, reports[^1]);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task Decrypt_FlippedByteInSecondChunk_Throws()
    {
        string path = await EncryptAsync(RandomNumberGenerator.GetBytes(ContentTransfer.ChunkSize + 100));
        byte[] bytes = await File.ReadAllBytesAsync(path, Cancellation);
        bytes[HeaderBytes + ChunkOverheadBytes + ContentTransfer.ChunkSize + ChunkOverheadBytes + 5] ^= 0x01;
        await File.WriteAllBytesAsync(path, bytes, Cancellation);

        await Assert.ThrowsAsync<InvalidDataException>(() => ContentTransfer.DecryptAsync(path, Stream.Null, key, null, Cancellation));
    }

    [Fact]
    public async Task Decrypt_TruncatedLastChunk_Throws()
    {
        string path = await EncryptAsync(RandomNumberGenerator.GetBytes(ContentTransfer.ChunkSize + 100));
        using (FileStream file = new(path, FileMode.Open, FileAccess.Write))
        {
            file.SetLength(file.Length - 10);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => ContentTransfer.DecryptAsync(path, Stream.Null, key, null, Cancellation));
    }

    [Fact]
    public async Task Decrypt_SwappedChunks_Throws()
    {
        string path = await EncryptAsync(RandomNumberGenerator.GetBytes(ContentTransfer.ChunkSize * 2));
        byte[] bytes = await File.ReadAllBytesAsync(path, Cancellation);
        int chunk = ChunkOverheadBytes + ContentTransfer.ChunkSize;
        byte[] swapped = [.. bytes[..HeaderBytes], .. bytes[(HeaderBytes + chunk)..], .. bytes[HeaderBytes..(HeaderBytes + chunk)]];
        await File.WriteAllBytesAsync(path, swapped, Cancellation);

        await Assert.ThrowsAsync<InvalidDataException>(() => ContentTransfer.DecryptAsync(path, Stream.Null, key, null, Cancellation));
    }

    [Fact]
    public async Task Decrypt_AppendedChunk_Throws()
    {
        string path = await EncryptAsync(RandomNumberGenerator.GetBytes(ContentTransfer.ChunkSize * 2));
        byte[] bytes = await File.ReadAllBytesAsync(path, Cancellation);
        byte[] firstChunk = bytes[HeaderBytes..(HeaderBytes + ChunkOverheadBytes + ContentTransfer.ChunkSize)];
        await File.WriteAllBytesAsync(path, [.. bytes, .. firstChunk], Cancellation);

        await Assert.ThrowsAsync<InvalidDataException>(() => ContentTransfer.DecryptAsync(path, Stream.Null, key, null, Cancellation));
    }

    [Fact]
    public async Task Decrypt_DroppedLastChunk_Throws()
    {
        string path = await EncryptAsync(RandomNumberGenerator.GetBytes(ContentTransfer.ChunkSize + 100));
        using (FileStream file = new(path, FileMode.Open, FileAccess.Write))
        {
            file.SetLength(file.Length - (ChunkOverheadBytes + 100));
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => ContentTransfer.DecryptAsync(path, Stream.Null, key, null, Cancellation));
    }

    [Fact]
    public async Task IsComplete_RequiresMatchingLengthAndKey()
    {
        string path = await EncryptAsync(RandomNumberGenerator.GetBytes(ContentTransfer.ChunkSize + 100));
        byte[] bytes = await File.ReadAllBytesAsync(path, Cancellation);
        string truncated = Path.Combine(directory.Path, "truncated.bin");
        string extended = Path.Combine(directory.Path, "extended.bin");
        await File.WriteAllBytesAsync(truncated, bytes[..^1], Cancellation);
        await File.WriteAllBytesAsync(extended, [.. bytes, 0], Cancellation);

        Assert.True(ContentTransfer.IsComplete(path, key));
        Assert.False(ContentTransfer.IsComplete(path, RandomNumberGenerator.GetBytes(32)));
        Assert.False(ContentTransfer.IsComplete(truncated, key));
        Assert.False(ContentTransfer.IsComplete(extended, key));
        Assert.False(ContentTransfer.IsComplete(Path.Combine(directory.Path, "missing.bin"), key));
    }

    [Fact]
    public void DeriveContentKey_SameInputs_SameKey()
    {
        byte[] masterKey = RandomNumberGenerator.GetBytes(32);
        const string otherHash = "60303ae22b998861bce3b28f33eec1be758a213c86c93c076dbe9f558c11c752";

        byte[] first = ContentTransfer.DeriveContentKey(masterKey, Hash);

        Assert.Equal(32, first.Length);
        Assert.Equal(first, ContentTransfer.DeriveContentKey(masterKey, Hash));
        Assert.NotEqual(first, ContentTransfer.DeriveContentKey(masterKey, otherHash));
        Assert.NotEqual(first, ContentTransfer.DeriveContentKey(RandomNumberGenerator.GetBytes(32), Hash));
    }

    public void Dispose()
    {
        directory.Dispose();
    }

    private async Task<string> EncryptAsync(byte[] plaintext)
    {
        string path = Path.Combine(directory.Path, Hash + ".bin");
        using MemoryStream source = new(plaintext);
        await ContentTransfer.EncryptAsync(source, path, key, null, Cancellation);
        return path;
    }

    private sealed class InlineProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
