// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Security.Cryptography;
using Foundry.Core.Services.NetworkCache;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Download;
using Foundry.Utilities.Networking;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class ArtifactDownloadServiceNetworkTierTests : IDisposable
{
    private const string Url = "https://example.test/os/image.esd";
    private static readonly byte[] ShareBytes = [1, 2, 3, 4, 5];
    private static readonly byte[] HttpBytes = [9, 9, 9];
    private readonly string root = Path.Combine(Path.GetTempPath(), "foundry-nct-" + Guid.NewGuid().ToString("N"));
    private readonly string share;
    private readonly string destination;
    private readonly CountingHandler handler = new();
    private readonly NetworkCacheSessionAccessor accessor = new();

    public ArtifactDownloadServiceNetworkTierTests()
    {
        share = Path.Combine(root, "share");
        destination = Path.Combine(root, "local", "image.esd");
        Directory.CreateDirectory(share);
        accessor.Current = NetworkCacheSession.Connected(share, canWrite: false, new NoopConnector());
    }

    public void Dispose()
    {
        handler.Dispose();
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task Download_LocalHit_DoesNotTouchShare()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(destination, ShareBytes, TestContext.Current.CancellationToken);
        accessor.Current = NetworkCacheSession.Connected(Path.Combine(root, "missing-share"), canWrite: false, new NoopConnector());

        ArtifactDownloadResult result = await DownloadAsync(Sha256(ShareBytes));

        Assert.Equal("cache-hit", result.Method);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task Download_ShareHit_CopiesAndReturnsNetworkCacheMethod()
    {
        PlaceOnShare(Sha256(ShareBytes), ShareBytes);

        ArtifactDownloadResult result = await DownloadAsync(Sha256(ShareBytes));

        Assert.Equal("network-cache", result.Method);
        Assert.False(result.Downloaded);
        Assert.Equal(ShareBytes, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task Download_WhenShareCopyHashMismatches_FallsBackToHttp()
    {
        string expected = Sha256(HttpBytes);
        string sharedFile = PlaceOnShare(expected, ShareBytes);

        ArtifactDownloadResult result = await DownloadAsync(expected);

        Assert.Equal("httpclient", result.Method);
        Assert.Equal(1, handler.Requests);
        Assert.Equal(HttpBytes, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(sharedFile));
    }

    [Fact]
    public async Task Download_HashlessShareHitWithSidecar_IsUsed()
    {
        string sharedFile = PlaceOnShare(null, ShareBytes);
        await File.WriteAllTextAsync(sharedFile + NetworkCacheLayout.HashSidecarSuffix, Sha256(ShareBytes), TestContext.Current.CancellationToken);

        ArtifactDownloadResult result = await DownloadAsync(null);

        Assert.Equal("network-cache", result.Method);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task Download_HashlessShareHitWithoutSidecar_IsIgnored()
    {
        PlaceOnShare(null, ShareBytes);

        ArtifactDownloadResult result = await DownloadAsync(null);

        Assert.Equal("httpclient", result.Method);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task Download_NonCacheableKind_SkipsShare()
    {
        PlaceOnShare(Sha256(ShareBytes), ShareBytes, kind: "PostInstall");

        ArtifactDownloadResult result = await DownloadAsync(Sha256(HttpBytes), kind: "PostInstall");

        Assert.Equal("httpclient", result.Method);
    }

    [Fact]
    public async Task Download_NoSession_BehavesAsToday()
    {
        PlaceOnShare(Sha256(ShareBytes), ShareBytes);
        accessor.Current = null;

        ArtifactDownloadResult result = await DownloadAsync(Sha256(HttpBytes));

        Assert.Equal("httpclient", result.Method);
    }

    [Fact]
    public async Task Download_HashlessWithoutSize_SkipsShare()
    {
        string sharedFile = PlaceOnShare(null, ShareBytes, size: null);
        await File.WriteAllTextAsync(sharedFile + NetworkCacheLayout.HashSidecarSuffix, Sha256(ShareBytes), TestContext.Current.CancellationToken);

        ArtifactDownloadResult result = await DownloadAsync(null, size: null);

        Assert.Equal("httpclient", result.Method);
    }

    [Fact]
    public async Task Download_CancelledDuringShareCopy_DeletesLocalCopy()
    {
        PlaceOnShare(Sha256(ShareBytes), ShareBytes);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var progress = new SynchronousProgress(_ => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService().DownloadAsync(Url, destination, Sha256(ShareBytes), ShareBytes.Length, "OperatingSystemImage", cancellation.Token, progress));

        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task Download_WhenShareLookupHangs_FallsBackToHttpAndSkipsShareAfterwards()
    {
        PlaceOnShare(Sha256(HttpBytes), HttpBytes);
        int lookups = 0;
        var reader = new NetworkCacheReader(accessor, NullLogger<NetworkCacheReader>.Instance, TimeSpan.FromMilliseconds(100), path =>
        {
            Interlocked.Increment(ref lookups);
            Thread.Sleep(TimeSpan.FromSeconds(2));
            return true;
        });

        ArtifactDownloadResult first = await CreateService(reader).DownloadAsync(Url, destination, Sha256(HttpBytes), HttpBytes.Length, "OperatingSystemImage", TestContext.Current.CancellationToken);
        File.Delete(destination);
        ArtifactDownloadResult second = await CreateService(reader).DownloadAsync(Url, destination, Sha256(HttpBytes), HttpBytes.Length, "OperatingSystemImage", TestContext.Current.CancellationToken);

        Assert.Equal("httpclient", first.Method);
        Assert.Equal("httpclient", second.Method);
        Assert.Equal(1, lookups);
        Assert.False(accessor.Current!.IsAvailable);
    }

    [Fact]
    public async Task Download_HttpSuccess_EnqueuesPublication()
    {
        var publisher = new RecordingPublisher();

        await CreateService(publisher: publisher).DownloadAsync(Url, destination, Sha256(HttpBytes), HttpBytes.Length, "OperatingSystemImage", TestContext.Current.CancellationToken);

        NetworkCachePublication item = Assert.Single(publisher.Items);
        Assert.Equal(destination, item.LocalPath);
        Assert.Equal(NetworkCacheLayout.DownloadKey(Url, Sha256(HttpBytes), HttpBytes.Length), item.Key);
        Assert.Equal("image.esd", item.FileName);
        Assert.False(item.Hashless);
    }

    [Fact]
    public async Task Download_ShareHit_DoesNotEnqueue()
    {
        PlaceOnShare(Sha256(ShareBytes), ShareBytes);
        var publisher = new RecordingPublisher();

        await CreateService(publisher: publisher).DownloadAsync(Url, destination, Sha256(ShareBytes), ShareBytes.Length, "OperatingSystemImage", TestContext.Current.CancellationToken);

        Assert.Empty(publisher.Items);
    }

    [Fact]
    public async Task Download_NonCacheableKind_DoesNotEnqueue()
    {
        var publisher = new RecordingPublisher();

        await CreateService(publisher: publisher).DownloadAsync(Url, destination, Sha256(HttpBytes), HttpBytes.Length, "PostInstall", TestContext.Current.CancellationToken);

        Assert.Empty(publisher.Items);
    }

    private ArtifactDownloadService CreateService(NetworkCacheReader? reader = null, INetworkCachePublisher? publisher = null) => new(
        NullLogger<ArtifactDownloadService>.Instance,
        new HttpClient(handler, disposeHandler: false),
        networkCacheReader: reader ?? new NetworkCacheReader(accessor, NullLogger<NetworkCacheReader>.Instance),
        networkCachePublisher: publisher);

    private sealed class RecordingPublisher : INetworkCachePublisher
    {
        public List<NetworkCachePublication> Items { get; } = [];

        public void Enqueue(NetworkCachePublication publication) => Items.Add(publication);

        public Task DrainAsync(TimeSpan limit, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private Task<ArtifactDownloadResult> DownloadAsync(string? hash, string kind = "OperatingSystemImage", long? size = 5)
    {
        long? expectedSize = hash == Sha256(HttpBytes) ? HttpBytes.Length : size;
        return CreateService().DownloadAsync(Url, destination, hash, expectedSize, kind, TestContext.Current.CancellationToken);
    }

    private string PlaceOnShare(string? hash, byte[] bytes, string kind = "OperatingSystemImage", long? size = 5)
    {
        string directory = NetworkCacheLayout.DownloadDirectory(share, kind, NetworkCacheLayout.DownloadKey(Url, hash, hash is null ? size : bytes.Length));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "image.esd");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(HttpBytes) });
        }
    }

    private sealed class SynchronousProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }

    private sealed class NoopConnector : ISmbConnector
    {
        public SmbConnectResult Connect(string uncPath, string? userName, ReadOnlySpan<char> password) => new(SmbConnectStatus.Connected, 0);

        public void Disconnect(string uncPath)
        {
        }
    }
}
