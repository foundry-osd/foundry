// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.NetworkCache;
using Foundry.Core.Services.Sharing;
using Foundry.Utilities.Networking;

namespace Foundry.Core.Tests.NetworkCache;

public sealed class NetworkCacheContentPublisherTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "foundry-nccp-" + Guid.NewGuid().ToString("N"));
    private readonly FakeSmbConnector connector = new();
    private readonly PublicationSigningKey signer = PublicationSigningKey.Create();
    private readonly List<string> publishedRoots = [];

    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        signer.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task PublishAsync_PublishesToTheContentRootOfEveryShare()
    {
        NetworkCacheSettings settings = Settings(Share("s1"), Share("s2"));

        IReadOnlyList<NetworkCacheContentPublishResult> results = await Publisher().PublishAsync(settings, NoPassword, RecordAsync, null, Cancellation);

        Assert.Equal([NetworkCacheContentPublishStatus.Published, NetworkCacheContentPublishStatus.Published], results.Select(result => result.Status));
        Assert.Equal(["s1", "s2"], results.Select(result => result.Share.Id));
        Assert.Equal(settings.Shares.Select(share => NetworkCacheLayout.ContentRoot(share.Path)), publishedRoots);
        Assert.Equal(settings.Shares.Select(share => share.Path), connector.Disconnected);
    }

    [Fact]
    public async Task PublishAsync_OpensASessionOnlyForAShareWithAnAccount()
    {
        NetworkCacheSettings settings = Settings(Share("s1"), Share("s2", accountId: null));
        Directory.CreateDirectory(settings.Shares[1].Path);

        IReadOnlyList<NetworkCacheContentPublishResult> results =
            await Publisher().PublishAsync(settings, id => id == "a1" ? "Secret1!".ToCharArray() : null, RecordAsync, null, Cancellation);

        Assert.All(results, result => Assert.Equal(NetworkCacheContentPublishStatus.Published, result.Status));
        Assert.Equal([(settings.Shares[0].Path, @"CONTOSO\svc", "Secret1!")], connector.Connections);
        Assert.Equal([settings.Shares[0].Path], connector.Disconnected);
        Assert.Empty(connector.ForcedDisconnects);
    }

    [Fact]
    public async Task PublishAsync_ClearsThePasswordCopyAfterConnecting()
    {
        char[] password = "Secret1!".ToCharArray();
        NetworkCacheSettings settings = Settings(Share("s1"));

        await Publisher().PublishAsync(settings, _ => password, RecordAsync, null, Cancellation);

        Assert.All(password, character => Assert.Equal('\0', character));
    }

    [Fact]
    public async Task PublishAsync_ReportsAnUnreachableShareAndContinuesWithTheNext()
    {
        NetworkCacheSettings settings = Settings(Share("s1"), Share("s2"));
        connector.Statuses[settings.Shares[0].Path] = new SmbConnectResult(SmbConnectStatus.NotFound, 53);

        IReadOnlyList<NetworkCacheContentPublishResult> results = await Publisher().PublishAsync(settings, NoPassword, RecordAsync, null, Cancellation);

        Assert.Equal(NetworkCacheContentPublishStatus.Unreachable, results[0].Status);
        Assert.Equal(53, results[0].Connection?.Win32Error);
        Assert.Equal(NetworkCacheContentPublishStatus.Published, results[1].Status);
        Assert.Equal([NetworkCacheLayout.ContentRoot(settings.Shares[1].Path)], publishedRoots);
        Assert.Equal([settings.Shares[1].Path], connector.Disconnected);
    }

    [Fact]
    public async Task PublishAsync_ReportsAFailedPublicationWithoutThrowing()
    {
        NetworkCacheSettings settings = Settings(Share("s1"), Share("s2"));
        var failure = new IOException("The share is full.");

        IReadOnlyList<NetworkCacheContentPublishResult> results = await Publisher().PublishAsync(settings, NoPassword, (contentRoot, cancellationToken) =>
            contentRoot.StartsWith(settings.Shares[0].Path, StringComparison.OrdinalIgnoreCase) ? throw failure : RecordAsync(contentRoot, cancellationToken), null, Cancellation);

        Assert.Equal(NetworkCacheContentPublishStatus.Failed, results[0].Status);
        Assert.Same(failure, results[0].Error);
        Assert.Equal(NetworkCacheContentPublishStatus.Published, results[1].Status);
        Assert.Equal(settings.Shares.Select(share => share.Path), connector.Disconnected);
    }

    [Fact]
    public async Task PublishAsync_PropagatesCancellationAndDisconnects()
    {
        NetworkCacheSettings settings = Settings(Share("s1"), Share("s2"));
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Publisher().PublishAsync(settings, NoPassword, (_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }, null, cancellation.Token));

        Assert.Equal([settings.Shares[0].Path], connector.Disconnected);
    }

    [Fact]
    public async Task PublishAsync_DoesNothingWhenTheNetworkCacheIsDisabled()
    {
        NetworkCacheSettings settings = Settings(Share("s1")) with { IsEnabled = false };

        IReadOnlyList<NetworkCacheContentPublishResult> results = await Publisher().PublishAsync(settings, NoPassword, RecordAsync, null, Cancellation);

        Assert.Empty(results);
        Assert.Empty(connector.Connections);
        Assert.False(NetworkCacheContentPublisher.IsActive(settings));
        Assert.True(NetworkCacheContentPublisher.IsActive(settings with { IsEnabled = true }));
        Assert.False(NetworkCacheContentPublisher.IsActive(settings with { IsEnabled = true, Shares = [] }));
    }

    [Fact]
    public async Task PublishAsync_WritesTheSignedIndexAfterTheContentWithTheNextSequence()
    {
        NetworkCacheSettings settings = Settings(Share("s1"), Share("s2"));
        string contentRoot2 = NetworkCacheLayout.ContentRoot(settings.Shares[1].Path);
        await NetworkCacheIndexStore.WriteAsync(contentRoot2, signer.KeyId,
            NetworkCacheContentIndexCodec.Write(new NetworkCacheContentIndex { KeyId = signer.KeyId, Sequence = 7 }, signer), Cancellation);
        List<long> sequenceOnDiskDuringContent = [];

        IReadOnlyList<NetworkCacheContentPublishResult> results = await Publisher().PublishAsync(settings, NoPassword, (contentRoot, token) =>
        {
            sequenceOnDiskDuringContent.Add(File.Exists(IndexPath(contentRoot)) ? ReadIndex(contentRoot).Sequence : 0);
            return RecordAsync(contentRoot, token);
        }, Publication(new Dictionary<string, long> { ["s1"] = 3 }), Cancellation);

        Assert.Equal([0L, 7L], sequenceOnDiskDuringContent);
        Assert.Equal([4L, 8L], results.Select(result => result.IndexSequence));
        Assert.All(results, result => Assert.Equal(NetworkCacheContentPublishStatus.Published, result.Status));
        foreach ((NetworkCacheShare share, long sequence) in settings.Shares.Zip(new long[] { 4, 8 }))
        {
            NetworkCacheContentIndex index = ReadIndex(NetworkCacheLayout.ContentRoot(share.Path));
            Assert.Equal(sequence, index.Sequence);
            Assert.Equal(signer.KeyId, index.KeyId);
            Assert.Equal("manifest-1", index.CustomImages.ManifestId);
        }
    }

    [Fact]
    public async Task PublishAsync_ContinuesFromTheRecordedSequenceWhenTheShareHasNoIndex()
    {
        NetworkCacheSettings settings = Settings(Share("s1"));

        IReadOnlyList<NetworkCacheContentPublishResult> results = await Publisher().PublishAsync(
            settings, NoPassword, RecordAsync, Publication(new Dictionary<string, long> { ["s1"] = 41 }), Cancellation);

        Assert.Equal(42, results[0].IndexSequence);
        Assert.Equal(42, ReadIndex(NetworkCacheLayout.ContentRoot(settings.Shares[0].Path)).Sequence);
    }

    [Fact]
    public async Task PublishAsync_WritesNoIndexWhenTheContentPublicationFails()
    {
        NetworkCacheSettings settings = Settings(Share("s1"));

        IReadOnlyList<NetworkCacheContentPublishResult> results = await Publisher().PublishAsync(
            settings, NoPassword, (_, _) => throw new IOException("The share is full."), Publication(new Dictionary<string, long>()), Cancellation);

        Assert.Equal(NetworkCacheContentPublishStatus.Failed, results[0].Status);
        Assert.Null(results[0].IndexSequence);
        Assert.False(File.Exists(IndexPath(NetworkCacheLayout.ContentRoot(settings.Shares[0].Path))));
    }

    [Fact]
    public async Task PublishAsync_WritesNoIndexForAFollower()
    {
        NetworkCacheSettings settings = Settings(Share("s1"));

        IReadOnlyList<NetworkCacheContentPublishResult> results = await Publisher().PublishAsync(settings, NoPassword, RecordAsync, null, Cancellation);

        Assert.Equal(NetworkCacheContentPublishStatus.Published, results[0].Status);
        Assert.Null(results[0].IndexSequence);
        Assert.False(Directory.Exists(Path.Combine(NetworkCacheLayout.ContentRoot(settings.Shares[0].Path), NetworkCacheIndexStore.FolderName)));
    }

    [Fact]
    public async Task PublishAsync_WritesTheIndexEvenWhenAllThreeSectionsAreEmpty()
    {
        NetworkCacheSettings settings = Settings(Share("s1"));
        var empty = new NetworkCacheIndexPublication(signer, new NetworkCacheContentSections(new(), new(), new()), new Dictionary<string, long>());

        IReadOnlyList<NetworkCacheContentPublishResult> results = await Publisher().PublishAsync(settings, NoPassword, RecordAsync, empty, Cancellation);

        Assert.Equal(1, results[0].IndexSequence);
        NetworkCacheContentIndex index = ReadIndex(NetworkCacheLayout.ContentRoot(settings.Shares[0].Path));
        Assert.False(index.CustomImages.IsEnabled);
        Assert.Empty(index.CustomDrivers.Sets);
        Assert.Empty(index.PreOobe.Actions);
    }

    [Fact]
    public async Task PublishAsync_ReportsAFailedIndexWriteAsFailedAndContinuesWithTheNextShare()
    {
        NetworkCacheSettings settings = Settings(Share("s1"), Share("s2"));

        IReadOnlyList<NetworkCacheContentPublishResult> results = await Publisher().PublishAsync(settings, NoPassword, (contentRoot, token) =>
        {
            // A file where the Index folder belongs makes the index write fail after the content succeeded.
            if (contentRoot.StartsWith(settings.Shares[0].Path, StringComparison.OrdinalIgnoreCase)) File.WriteAllText(Path.Combine(contentRoot, NetworkCacheIndexStore.FolderName), "x");
            return RecordAsync(contentRoot, token);
        }, Publication(new Dictionary<string, long>()), Cancellation);

        Assert.Equal(NetworkCacheContentPublishStatus.Failed, results[0].Status);
        Assert.NotNull(results[0].Error);
        Assert.Null(results[0].IndexSequence);
        Assert.Equal(NetworkCacheContentPublishStatus.Published, results[1].Status);
        Assert.Equal(1, results[1].IndexSequence);
    }

    [Fact]
    public async Task PublishAsync_SkipsTheIndexOfAnUnreachableShareAndWritesTheNext()
    {
        NetworkCacheSettings settings = Settings(Share("s1"), Share("s2"));
        connector.Statuses[settings.Shares[0].Path] = new SmbConnectResult(SmbConnectStatus.NotFound, 53);

        IReadOnlyList<NetworkCacheContentPublishResult> results = await Publisher().PublishAsync(settings, NoPassword, RecordAsync, Publication(new Dictionary<string, long>()), Cancellation);

        Assert.Null(results[0].IndexSequence);
        Assert.Equal(1, results[1].IndexSequence);
    }

    private NetworkCacheContentPublisher Publisher() => new(connector);

    private NetworkCacheIndexPublication Publication(IReadOnlyDictionary<string, long> recordedSequences) => new(
        signer,
        new NetworkCacheContentSections(new() { IsEnabled = true, ManifestId = "manifest-1" }, new(), new()),
        recordedSequences);

    private string IndexPath(string contentRoot) => Path.Combine(contentRoot, NetworkCacheIndexStore.FolderName, signer.KeyId + ".json");

    private NetworkCacheContentIndex ReadIndex(string contentRoot)
    {
        byte[] file = File.ReadAllBytes(IndexPath(contentRoot));
        Assert.Equal(NetworkCacheIndexReadStatus.Valid, NetworkCacheContentIndexCodec.Read(file, signer.PublicKey, 0, out NetworkCacheContentIndex? index));
        return index!;
    }

    private static char[]? NoPassword(string accountId) => null;

    private Task RecordAsync(string contentRoot, CancellationToken cancellationToken)
    {
        publishedRoots.Add(contentRoot);
        return Task.CompletedTask;
    }

    private static NetworkCacheSettings Settings(params NetworkCacheShare[] shares) => new()
    {
        IsEnabled = true,
        Shares = shares,
        Accounts = [new NetworkCacheAccount { Id = "a1", UserName = @"CONTOSO\svc" }]
    };

    private NetworkCacheShare Share(string id, string? accountId = "a1") => new() { Id = id, Path = Path.Combine(root, id), AccountId = accountId };

    private sealed class FakeSmbConnector : ISmbConnector
    {
        public Dictionary<string, SmbConnectResult> Statuses { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Path, string? UserName, string Password)> Connections { get; } = [];
        public List<string> Disconnected { get; } = [];

        public SmbConnectResult Connect(string uncPath, string? userName, ReadOnlySpan<char> password)
        {
            lock (Connections) Connections.Add((uncPath, userName, password.ToString()));
            return Statuses.GetValueOrDefault(uncPath, new SmbConnectResult(SmbConnectStatus.Connected, 0));
        }

        public List<string> ForcedDisconnects { get; } = [];

        public void Disconnect(string uncPath) => Disconnect(uncPath, force: true);

        public void Disconnect(string uncPath, bool force)
        {
            lock (Disconnected)
            {
                Disconnected.Add(uncPath);
                if (force) ForcedDisconnects.Add(uncPath);
            }
        }
    }
}
