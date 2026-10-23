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

    private Task<ArtifactDownloadResult> DownloadAsync(string? hash, string kind = "OperatingSystemImage")
    {
        var service = new ArtifactDownloadService(
            NullLogger<ArtifactDownloadService>.Instance,
            new HttpClient(handler, disposeHandler: false),
            networkCacheReader: new NetworkCacheReader(accessor, NullLogger<NetworkCacheReader>.Instance));
        long size = hash == Sha256(HttpBytes) ? HttpBytes.Length : ShareBytes.Length;
        return service.DownloadAsync(Url, destination, hash, hash is null ? null : size, kind, TestContext.Current.CancellationToken);
    }

    private string PlaceOnShare(string? hash, byte[] bytes, string kind = "OperatingSystemImage")
    {
        string directory = NetworkCacheLayout.DownloadDirectory(share, kind, NetworkCacheLayout.DownloadKey(Url, hash, hash is null ? null : bytes.Length));
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

    private sealed class NoopConnector : ISmbConnector
    {
        public SmbConnectResult Connect(string uncPath, string? userName, ReadOnlySpan<char> password) => new(SmbConnectStatus.Connected, 0);

        public void Disconnect(string uncPath)
        {
        }
    }
}
