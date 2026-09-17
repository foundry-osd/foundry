// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Deploy.Services.Download;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class ArtifactDownloadServiceTests
{
    [Fact]
    public async Task DownloadAsync_WhenHeadersStall_TimesOutWithoutStartingBodyOrRetrying()
    {
        using TempDirectory temp = TempDirectory.Create();
        string destination = Path.Combine(temp.Path, "image.esd");
        var clock = new DeadlineTimeProvider();
        using var handler = new StalledHeadersHandler(clock);
        using var client = new HttpClient(handler);
        var service = new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, client, clock);

        await Assert.ThrowsAsync<TimeoutException>(() => service.DownloadAsync(
            "https://example.test/image.esd", destination, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(1, handler.RequestCount);
        Assert.False(File.Exists(destination));
    }

    private sealed class StalledHeadersHandler(DeadlineTimeProvider clock) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            clock.Fire(1);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("A stalled header request should be cancelled.");
        }
    }

    [Fact]
    public async Task DownloadAsync_WhenRealServerStallsAfterHeaders_DeadlineStopsBodyRead()
    {
        using TempDirectory temp = TempDirectory.Create();
        string destination = Path.Combine(temp.Path, "image.esd");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var clock = new DeadlineTimeProvider();
        using var handler = new SocketsHttpHandler { UseProxy = false };
        using var client = new HttpClient(handler);
        var service = new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, client, clock);
        var bodyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<ArtifactDownloadResult> transfer = service.DownloadAsync(
            $"http://127.0.0.1:{port}/image.esd", destination, cancellationToken: caller.Token,
            progress: new InlineProgress<DownloadProgress>(value => { if (value.BytesProcessed > 0) bodyStarted.TrySetResult(); }));
        try
        {
            using TcpClient server = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await server.GetStream().WriteAsync(
                Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 1000\r\n\r\nx"), TestContext.Current.CancellationToken);
            await bodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            clock.Fire(1);
            TimeoutException failure = await Assert.ThrowsAsync<TimeoutException>(() => transfer.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.True(transfer.IsCompleted);
            Assert.Contains("inactivity", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(destination));
        }
        finally
        {
            caller.Cancel();
            try { await transfer; } catch (Exception) { }
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public async Task DownloadAsync_WhenBodyStalls_TimesOutAndRemovesPartialFile(int timerIndex, bool partialBody)
    {
        using TempDirectory temp = TempDirectory.Create();
        string destination = Path.Combine(temp.Path, "image.esd");
        var clock = new DeadlineTimeProvider();
        using var stream = new StallingStream(partialBody, () => clock.Fire(timerIndex));
        using var handler = new StreamHttpMessageHandler(stream);
        using var client = new HttpClient(handler);
        var service = new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, client, clock);

        await Assert.ThrowsAsync<TimeoutException>(() => service.DownloadAsync(
            "https://example.test/image.esd", destination, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(1, handler.RequestCount);
        Assert.True(stream.Disposed);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task DownloadAsync_WhenCancelledDuringBodyRead_RemovesPartialFileWithoutRetry()
    {
        using TempDirectory temp = TempDirectory.Create();
        string destination = Path.Combine(temp.Path, "image.esd");
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var stream = new StallingStream(true, caller.Cancel);
        using var handler = new StreamHttpMessageHandler(stream);
        using var client = new HttpClient(handler);
        var service = new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(
            "https://example.test/image.esd", destination, cancellationToken: caller.Token));

        Assert.Equal(1, handler.RequestCount);
        Assert.True(stream.Disposed);
        Assert.False(File.Exists(destination));
    }

    private sealed class DeadlineTimeProvider : TimeProvider
    {
        private readonly List<Action> _callbacks = [];
        public void Fire(int index) => _callbacks[index]();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callbacks.Add(() => callback(state));
            return new DeadlineTimer();
        }
        private sealed class DeadlineTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class StreamHttpMessageHandler(Stream stream) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
        }
    }

    private sealed class StallingStream(bool partialBody, Action stalled) : Stream
    {
        private bool _hasRead;
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (partialBody && !_hasRead)
            {
                _hasRead = true;
                buffer.Span[0] = 42;
                return 1;
            }
            stalled();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("SHA1", false)]
    [InlineData("SHA256", false)]
    [InlineData("SHA1", true)]
    [InlineData("SHA256", true)]
    public async Task DownloadAsync_WhenCacheBytesMatch_ReusesCache(string algorithm, bool withManifest)
    {
        using TempDirectory temp = TempDirectory.Create();
        string destinationPath = Path.Combine(temp.Path, "install.esd");
        byte[] content = Encoding.UTF8.GetBytes("valid cached content");
        await File.WriteAllBytesAsync(destinationPath, content, TestContext.Current.CancellationToken);
        string expectedHash = ComputeHash(content, algorithm);
        if (withManifest)
        {
            await WriteLegacyManifestAsync(destinationPath, expectedHash, algorithm);
        }

        using var client = new HttpClient(new ThrowingHttpMessageHandler());
        var service = new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, client);

        ArtifactDownloadResult result = await service.DownloadAsync(
            "https://example.test/install.esd",
            destinationPath,
            expectedHash.ToLowerInvariant(),
            expectedSizeBytes: content.Length,
            artifactKind: "OperatingSystemImage",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Downloaded);
        Assert.Equal("cache-hit", result.Method);
        Assert.Equal(content.Length, result.SizeBytes);
        Assert.Equal(content, await File.ReadAllBytesAsync(result.DestinationPath, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("SHA1")]
    [InlineData("SHA256")]
    public async Task DownloadAsync_WhenManifestIsForged_RedownloadsAndVerifiesBytes(string algorithm)
    {
        using TempDirectory temp = TempDirectory.Create();
        string destinationPath = Path.Combine(temp.Path, "install.esd");
        byte[] content = Encoding.UTF8.GetBytes("tampered-content");
        byte[] expectedContent = Encoding.UTF8.GetBytes("original-content");
        string expectedHash = ComputeHash(expectedContent, algorithm);
        await File.WriteAllBytesAsync(destinationPath, content, TestContext.Current.CancellationToken);
        DateTimeOffset lastWriteTime = DateTimeOffset.UtcNow.AddMinutes(-3);
        File.SetLastWriteTimeUtc(destinationPath, lastWriteTime.UtcDateTime);

        await WriteLegacyManifestAsync(destinationPath, expectedHash, algorithm);

        var handler = new StaticHttpMessageHandler(expectedContent);
        using var client = new HttpClient(handler);
        var service = new ArtifactDownloadService(
            NullLogger<ArtifactDownloadService>.Instance,
            client);

        ArtifactDownloadResult result = await service.DownloadAsync(
            "https://example.test/install.esd",
            destinationPath,
            expectedHash,
            expectedSizeBytes: content.Length,
            artifactKind: "OperatingSystemImage",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Downloaded);
        Assert.Equal("httpclient", result.Method);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(expectedContent, await File.ReadAllBytesAsync(result.DestinationPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadAsync_WhenExistingFileSizeDiffers_RedownloadsWithoutHashingCache()
    {
        using TempDirectory temp = TempDirectory.Create();
        string destinationPath = Path.Combine(temp.Path, "driver.cab");
        await File.WriteAllTextAsync(destinationPath, "bad", TestContext.Current.CancellationToken);

        byte[] downloadedContent = Encoding.UTF8.GetBytes("valid driver payload");
        string expectedHash = ComputeSha256(downloadedContent);
        var handler = new StaticHttpMessageHandler(downloadedContent);
        var service = new ArtifactDownloadService(
            NullLogger<ArtifactDownloadService>.Instance,
            new HttpClient(handler));

        ArtifactDownloadResult result = await service.DownloadAsync(
            "https://example.test/driver.cab",
            destinationPath,
            expectedHash,
            expectedSizeBytes: downloadedContent.Length,
            artifactKind: "OemDriverPack",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Downloaded);
        Assert.Equal("httpclient", result.Method);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(downloadedContent, await File.ReadAllBytesAsync(destinationPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadAsync_WhenDownloadedHashMatches_ReturnsVerifiedContent()
    {
        using TempDirectory temp = TempDirectory.Create();
        string destinationPath = Path.Combine(temp.Path, "firmware.cab");
        byte[] downloadedContent = Encoding.UTF8.GetBytes("firmware payload");
        string expectedHash = ComputeSha256(downloadedContent);
        var service = new ArtifactDownloadService(
            NullLogger<ArtifactDownloadService>.Instance,
            new HttpClient(new StaticHttpMessageHandler(downloadedContent)));
        var reports = new List<DownloadProgress>();

        ArtifactDownloadResult result = await service.DownloadAsync(
            "https://example.test/firmware.cab",
            destinationPath,
            expectedHash,
            expectedSizeBytes: downloadedContent.Length,
            artifactKind: "MicrosoftUpdateCatalogFirmware",
            cancellationToken: TestContext.Current.CancellationToken,
            progress: new InlineProgress<DownloadProgress>(reports.Add));

        Assert.True(result.Downloaded);
        Assert.Equal(downloadedContent.Length, result.SizeBytes);
        Assert.Equal(downloadedContent, await File.ReadAllBytesAsync(result.DestinationPath, TestContext.Current.CancellationToken));
        Assert.All(reports, value => Assert.Equal(DownloadPhase.Downloading, value.Phase));
        Assert.Equal(0, reports[0].BytesProcessed);
        Assert.Equal(downloadedContent.Length, reports[^1].BytesProcessed);
    }

    [Fact]
    public async Task DownloadAsync_WhenDownloadedHashDiffers_ThrowsHashVerificationError()
    {
        using TempDirectory temp = TempDirectory.Create();
        string destinationPath = Path.Combine(temp.Path, "driver.cab");
        byte[] downloadedContent = Encoding.UTF8.GetBytes("unexpected payload");
        var service = new ArtifactDownloadService(
            NullLogger<ArtifactDownloadService>.Instance,
            new HttpClient(new StaticHttpMessageHandler(downloadedContent)));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadAsync(
                "https://example.test/driver.cab",
                destinationPath,
                new string('B', 64),
                expectedSizeBytes: downloadedContent.Length,
                artifactKind: "MicrosoftUpdateCatalogDriver",
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Hash verification failed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SHA1")]
    [InlineData("SHA256")]
    public async Task DownloadAsync_WhenCacheChangesWithOriginalSizeAndTimestamp_Redownloads(string algorithm)
    {
        using TempDirectory temp = TempDirectory.Create();
        string destinationPath = Path.Combine(temp.Path, "driver.exe");
        byte[] content = Encoding.UTF8.GetBytes("original-content");
        string expectedHash = ComputeHash(content, algorithm);
        var handler = new StaticHttpMessageHandler(content);
        using var client = new HttpClient(handler);
        var service = new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, client);
        await service.DownloadAsync("https://example.test/driver.exe", destinationPath, expectedHash,
            cancellationToken: TestContext.Current.CancellationToken);
        await WriteLegacyManifestAsync(destinationPath, expectedHash, algorithm);
        DateTime originalTimestamp = File.GetLastWriteTimeUtc(destinationPath);
        byte[] changedContent = (byte[])content.Clone();
        changedContent[0] ^= 0xFF;
        await File.WriteAllBytesAsync(destinationPath, changedContent, TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(destinationPath, originalTimestamp);

        ArtifactDownloadResult result = await service.DownloadAsync(
            "https://example.test/driver.exe", destinationPath, expectedHash,
            expectedSizeBytes: content.Length, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Downloaded);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(content, await File.ReadAllBytesAsync(result.DestinationPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadAsync_WhenForgedCacheAndReplacementAreInvalid_ThrowsHashVerificationError()
    {
        using TempDirectory temp = TempDirectory.Create();
        string destinationPath = Path.Combine(temp.Path, "driver.cab");
        byte[] content = Encoding.UTF8.GetBytes("unexpected payload");
        await File.WriteAllBytesAsync(destinationPath, content, TestContext.Current.CancellationToken);
        string expectedHash = new('B', 64);
        await WriteLegacyManifestAsync(destinationPath, expectedHash, "SHA256");
        using var client = new HttpClient(new StaticHttpMessageHandler(content));
        var service = new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, client);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadAsync("https://example.test/driver.cab", destinationPath, expectedHash,
                expectedSizeBytes: content.Length, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Hash verification failed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadAsync_WhenCacheVerificationIsCancelled_DoesNotReturnCacheHitOrDownload(bool cancelDuringHashing)
    {
        using TempDirectory temp = TempDirectory.Create();
        string destinationPath = Path.Combine(temp.Path, "install.esd");
        byte[] content = new byte[256 * 1024];
        new Random(41).NextBytes(content);
        await File.WriteAllBytesAsync(destinationPath, content, TestContext.Current.CancellationToken);
        string expectedHash = ComputeSha256(content);
        await WriteLegacyManifestAsync(destinationPath, expectedHash, "SHA256");
        using var client = new HttpClient(new ThrowingHttpMessageHandler());
        var service = new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, client);
        using var cancellation = new CancellationTokenSource();
        if (!cancelDuringHashing)
        {
            cancellation.Cancel();
        }

        var reports = new List<DownloadProgress>();
        var progress = new InlineProgress<DownloadProgress>(value =>
        {
            reports.Add(value);
            if (cancelDuringHashing && value.BytesProcessed > 0 && value.BytesProcessed < content.Length)
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.DownloadAsync("https://example.test/install.esd", destinationPath, expectedHash,
                cancellationToken: cancellation.Token, progress: progress));
        Assert.Equal(content, await File.ReadAllBytesAsync(destinationPath, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(reports, value => value.BytesProcessed == content.Length);
    }

    [Theory]
    [InlineData("SHA1")]
    [InlineData("SHA256")]
    public async Task DownloadAsync_WhenVerifyingCache_ReportsActualIntermediateByteCounts(string algorithm)
    {
        using TempDirectory temp = TempDirectory.Create();
        string destinationPath = Path.Combine(temp.Path, "install.esd");
        byte[] content = new byte[256 * 1024];
        new Random(73).NextBytes(content);
        await File.WriteAllBytesAsync(destinationPath, content, TestContext.Current.CancellationToken);
        using var client = new HttpClient(new ThrowingHttpMessageHandler());
        var service = new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, client);
        var reports = new List<DownloadProgress>();

        ArtifactDownloadResult result = await service.DownloadAsync(
            "https://example.test/install.esd", destinationPath, ComputeHash(content, algorithm),
            cancellationToken: TestContext.Current.CancellationToken,
            progress: new InlineProgress<DownloadProgress>(reports.Add));

        Assert.False(result.Downloaded);
        Assert.Equal(0, reports[0].BytesProcessed);
        Assert.Contains(reports, value => value.BytesProcessed > 0 && value.BytesProcessed < content.Length);
        Assert.All(reports, value => Assert.Equal(content.Length, value.TotalBytes));
        Assert.All(reports, value => Assert.Equal(DownloadPhase.VerifyingCache, value.Phase));
        Assert.Equal(content.Length, reports[^1].BytesProcessed);
        Assert.Equal(reports.Select(value => value.BytesProcessed).Order(), reports.Select(value => value.BytesProcessed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadAsync_WhenLegacySidecarCannotBeUsed_ReusesValidBytes(bool sidecarIsDirectory)
    {
        using TempDirectory temp = TempDirectory.Create();
        string destinationPath = Path.Combine(temp.Path, "install.esd");
        byte[] content = Encoding.UTF8.GetBytes("valid cached content");
        await File.WriteAllBytesAsync(destinationPath, content, TestContext.Current.CancellationToken);
        if (sidecarIsDirectory)
        {
            Directory.CreateDirectory($"{destinationPath}.manifest.json");
        }
        else
        {
            await File.WriteAllTextAsync($"{destinationPath}.manifest.json", "{invalid", TestContext.Current.CancellationToken);
        }

        using var client = new HttpClient(new ThrowingHttpMessageHandler());
        var service = new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, client);
        ArtifactDownloadResult result = await service.DownloadAsync(
            "https://example.test/install.esd", destinationPath, ComputeSha256(content),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Downloaded);
        Assert.Equal(content, await File.ReadAllBytesAsync(result.DestinationPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadAsync_WhenLegacyArtifactHasNoExpectedHash_PreservesSizeBasedReuse()
    {
        using TempDirectory temp = TempDirectory.Create();
        string destinationPath = Path.Combine(temp.Path, "driver.cab");
        byte[] content = Encoding.UTF8.GetBytes("legacy payload without catalog digest");
        await File.WriteAllBytesAsync(destinationPath, content, TestContext.Current.CancellationToken);
        using var client = new HttpClient(new ThrowingHttpMessageHandler());
        var service = new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, client);
        var reports = new List<DownloadProgress>();

        ArtifactDownloadResult result = await service.DownloadAsync(
            "https://example.test/driver.cab", destinationPath, expectedSizeBytes: content.Length,
            cancellationToken: TestContext.Current.CancellationToken,
            progress: new InlineProgress<DownloadProgress>(reports.Add));

        Assert.False(result.Downloaded);
        Assert.Equal(content, await File.ReadAllBytesAsync(result.DestinationPath, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(reports, value => value.Phase == DownloadPhase.VerifyingCache);
    }

    internal static async Task WriteLegacyManifestAsync(string destinationPath, string expectedHash, string algorithm)
    {
        FileInfo artifact = new(destinationPath);
        await using FileStream stream = File.Create($"{destinationPath}.manifest.json");
        await JsonSerializer.SerializeAsync(
            stream,
            new
            {
                Version = 1,
                ArtifactKind = "OperatingSystemImage",
                SourceUrl = "https://example.test/install.esd",
                HashAlgorithm = algorithm,
                ExpectedHash = expectedHash,
                ExpectedSizeBytes = artifact.Length,
                FileSizeBytes = artifact.Length,
                FileLastWriteTimeUtc = new DateTimeOffset(artifact.LastWriteTimeUtc, TimeSpan.Zero),
                ValidatedAtUtc = DateTimeOffset.UtcNow,
                ValidatedBy = "Foundry.Deploy"
            },
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private static string ComputeHash(byte[] content, string algorithm) =>
        Convert.ToHexString(algorithm == "SHA1" ? SHA1.HashData(content) : SHA256.HashData(content));

    private static string ComputeSha256(byte[] content)
    {
        return Convert.ToHexString(SHA256.HashData(content));
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class StaticHttpMessageHandler(byte[] content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("HTTP must not be used for a valid cache hit.");
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
            Directory.CreateDirectory(path);
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            return new TempDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"foundry-artifact-cache-{Guid.NewGuid():N}"));
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
