// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.NetworkCache;
using Foundry.Core.Services.Sharing;

namespace Foundry.Core.Tests.NetworkCache;

public sealed class NetworkCacheIndexStoreTests : IDisposable
{
    private readonly string contentRoot = Path.Combine(Path.GetTempPath(), "foundry-ncis-" + Guid.NewGuid().ToString("N"), NetworkCacheLayout.RootFolderName);
    private readonly PublicationSigningKey signer = PublicationSigningKey.Create();

    private CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private string IndexFolder => Path.Combine(contentRoot, NetworkCacheIndexStore.FolderName);

    public void Dispose()
    {
        signer.Dispose();
        string parent = Path.GetDirectoryName(contentRoot)!;
        if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
    }

    [Fact]
    public async Task WriteAsync_LeavesOnlyTheFinalFileAndReplacesAnExistingOne()
    {
        await NetworkCacheIndexStore.WriteAsync(contentRoot, signer.KeyId, Signed(1), Cancellation);
        byte[] second = Signed(2);

        await NetworkCacheIndexStore.WriteAsync(contentRoot, signer.KeyId, second, Cancellation);

        Assert.Equal([Path.Combine(IndexFolder, signer.KeyId + ".json")], Directory.GetFileSystemEntries(IndexFolder));
        Assert.Equal(second, await File.ReadAllBytesAsync(Path.Combine(IndexFolder, signer.KeyId + ".json"), Cancellation));
    }

    [Fact]
    public async Task WriteAsync_SucceedsWhenAReaderOfTheIndexReleasesItWithinTheRetryWindow()
    {
        await NetworkCacheIndexStore.WriteAsync(contentRoot, signer.KeyId, Signed(1), Cancellation);
        byte[] second = Signed(2);
        var reader = new FileStream(Path.Combine(IndexFolder, signer.KeyId + ".json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        Task write = NetworkCacheIndexStore.WriteAsync(contentRoot, signer.KeyId, second, Cancellation);
        await Task.Delay(300, Cancellation);
        reader.Dispose();
        await write;

        Assert.Equal(second, await NetworkCacheIndexStore.ReadAsync(contentRoot, signer.KeyId, Cancellation));
    }

    [Fact]
    public async Task WriteAsync_RetriesWhileAReaderHoldsTheFileWithoutDeleteSharing()
    {
        await NetworkCacheIndexStore.WriteAsync(contentRoot, signer.KeyId, Signed(1), Cancellation);
        byte[] second = Signed(2);
        var holder = new FileStream(Path.Combine(IndexFolder, signer.KeyId + ".json"), FileMode.Open, FileAccess.Read, FileShare.Read);

        Task write = NetworkCacheIndexStore.WriteAsync(contentRoot, signer.KeyId, second, Cancellation);
        await Task.Delay(300, Cancellation);
        holder.Dispose();
        await write;

        Assert.Equal(second, await NetworkCacheIndexStore.ReadAsync(contentRoot, signer.KeyId, Cancellation));
        Assert.Single(Directory.GetFileSystemEntries(IndexFolder));
    }

    [Fact]
    public async Task WriteAsync_FailsAndLeavesNoTemporaryFileWhenTheFileStaysLocked()
    {
        byte[] first = Signed(1);
        await NetworkCacheIndexStore.WriteAsync(contentRoot, signer.KeyId, first, Cancellation);

        using (new FileStream(Path.Combine(IndexFolder, signer.KeyId + ".json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAsync<IOException>(() => NetworkCacheIndexStore.WriteAsync(contentRoot, signer.KeyId, Signed(2), Cancellation));
        }

        Assert.Equal(first, await NetworkCacheIndexStore.ReadAsync(contentRoot, signer.KeyId, Cancellation));
        Assert.Single(Directory.GetFileSystemEntries(IndexFolder));
    }

    [Fact]
    public async Task ReadAsync_ReturnsNullWithoutAFileAndIgnoresTemporaryFiles()
    {
        Assert.Null(await NetworkCacheIndexStore.ReadAsync(contentRoot, signer.KeyId, Cancellation));
        Directory.CreateDirectory(IndexFolder);
        await File.WriteAllBytesAsync(Path.Combine(IndexFolder, signer.KeyId + ".json.abcd.tmp"), Signed(1), Cancellation);

        Assert.Null(await NetworkCacheIndexStore.ReadAsync(contentRoot, signer.KeyId, Cancellation));
    }

    [Fact]
    public async Task ReadAsync_TreatsAFileOverTheMaximumAsMissing()
    {
        Directory.CreateDirectory(IndexFolder);
        await File.WriteAllBytesAsync(Path.Combine(IndexFolder, signer.KeyId + ".json"), new byte[NetworkCacheContentIndexCodec.MaximumBytes + 1], Cancellation);

        Assert.Null(await NetworkCacheIndexStore.ReadAsync(contentRoot, signer.KeyId, Cancellation));
    }

    [Fact]
    public async Task ReadSequenceAsync_ReturnsZeroWithoutAFileAndTheSequenceOfAValidFile()
    {
        Assert.Equal(0, await NetworkCacheIndexStore.ReadSequenceAsync(contentRoot, signer.PublicKey, Cancellation));

        await NetworkCacheIndexStore.WriteAsync(contentRoot, signer.KeyId, Signed(42), Cancellation);

        Assert.Equal(42, await NetworkCacheIndexStore.ReadSequenceAsync(contentRoot, signer.PublicKey, Cancellation));
    }

    [Fact]
    public async Task ReadSequenceAsync_ReturnsZeroForAFileTheTrustedKeyDoesNotVerify()
    {
        using PublicationSigningKey other = PublicationSigningKey.Create();
        await File.WriteAllBytesAsync(Path.Combine(Directory.CreateDirectory(IndexFolder).FullName, signer.KeyId + ".json"),
            NetworkCacheContentIndexCodec.Write(new NetworkCacheContentIndex { KeyId = other.KeyId, Sequence = 9 }, other), Cancellation);

        Assert.Equal(0, await NetworkCacheIndexStore.ReadSequenceAsync(contentRoot, signer.PublicKey, Cancellation));
    }

    [Fact]
    public async Task ReadAllUnverifiedAsync_ReturnsEveryWellFormedIndexAndSkipsTheRest()
    {
        using PublicationSigningKey other = PublicationSigningKey.Create();
        await NetworkCacheIndexStore.WriteAsync(contentRoot, signer.KeyId, Signed(3), Cancellation);
        await NetworkCacheIndexStore.WriteAsync(contentRoot, other.KeyId, NetworkCacheContentIndexCodec.Write(new NetworkCacheContentIndex { KeyId = other.KeyId, Sequence = 5 }, other), Cancellation);
        await File.WriteAllTextAsync(Path.Combine(IndexFolder, "broken.json"), "not json", Cancellation);
        await File.WriteAllBytesAsync(Path.Combine(IndexFolder, "huge.json"), new byte[NetworkCacheContentIndexCodec.MaximumBytes + 1], Cancellation);
        await File.WriteAllBytesAsync(Path.Combine(IndexFolder, signer.KeyId + ".json.abcd.tmp"), Signed(99), Cancellation);

        IReadOnlyList<NetworkCacheContentIndex> indexes = await NetworkCacheIndexStore.ReadAllUnverifiedAsync(contentRoot, Cancellation);

        Assert.Equal([3L, 5L], indexes.Select(index => index.Sequence).Order());
    }

    [Fact]
    public async Task ReadAllUnverifiedAsync_ReturnsNothingWithoutAnIndexFolder()
    {
        Assert.Empty(await NetworkCacheIndexStore.ReadAllUnverifiedAsync(contentRoot, Cancellation));
    }

    private byte[] Signed(long sequence) =>
        NetworkCacheContentIndexCodec.Write(new NetworkCacheContentIndex { KeyId = signer.KeyId, Sequence = sequence }, signer);
}
