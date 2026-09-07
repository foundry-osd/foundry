// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Foundry.Deploy.Services.Download;
using Foundry.Utilities.Networking;

namespace Foundry.Deploy.Tests;

public sealed class ArtifactAvailabilityProbeTests
{
    [Fact]
    public async Task EnsureAvailableAsync_RejectsMissingIntegrityBeforeHttp()
    {
        bool sent = false;
        using var client = new HttpClient(new Handler((_, _) => { sent = true; throw new InvalidOperationException("Unexpected HTTP call."); }));
        ArtifactIdentity artifact = Artifact() with { Integrity = new FileIntegrity(null, 3) };
        await Assert.ThrowsAsync<InvalidDataException>(() => new ArtifactAvailabilityProbe(client)
            .EnsureAvailableAsync(artifact, TestContext.Current.CancellationToken));
        Assert.False(sent);
    }

    [Theory]
    [InlineData("https://cdn.example.test/install.esd", true)]
    [InlineData("http://cdn.example.test/install.esd", false)]
    public async Task EnsureAvailableAsync_UsesValidatedRedirectPolicy(string target, bool permitted)
    {
        ArtifactIdentity artifact = Artifact();
        int sent = 0;
        var handler = new Handler((request, _) =>
        {
            sent++;
            Assert.Equal("bytes=0-0", request.Headers.Range?.ToString());
            if (sent == 1)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                redirect.Headers.Location = new Uri(target);
                return Task.FromResult(redirect);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) });
        });
        using var client = new HttpClient(new ValidatedRedirectHandler(handler, uri => ArtifactIntegrityPolicy.ValidateSourceUri(artifact, uri)));
        Task operation = new ArtifactAvailabilityProbe(client).EnsureAvailableAsync(artifact, TestContext.Current.CancellationToken);
        if (permitted) await operation;
        else await Assert.ThrowsAsync<InvalidDataException>(() => operation);
        Assert.Equal(permitted ? 2 : 1, sent);
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task EnsureAvailableAsync_RejectsOtherStatusCodes(HttpStatusCode status)
    {
        var body = new TrackingStream([1]);
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StreamContent(body) })));
        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() => new ArtifactAvailabilityProbe(client)
            .EnsureAvailableAsync(Artifact(), TestContext.Current.CancellationToken));
        Assert.Equal(status, exception.StatusCode);
        Assert.True(body.Disposed);
        Assert.Equal(0, body.BytesRead);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bytes 1-1/3")]
    [InlineData("items 0-0/3")]
    public async Task EnsureAvailableAsync_RejectsPartialResponseWithoutByteZero(string? range)
    {
        using var client = new HttpClient(new Handler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent([1]) };
            if (range is not null) response.Content.Headers.ContentRange = ContentRangeHeaderValue.Parse(range);
            return Task.FromResult(response);
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new ArtifactAvailabilityProbe(client)
            .EnsureAvailableAsync(Artifact(), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("http://example.test/install.esd")]
    [InlineData("https://user:password@example.test/install.esd")]
    public async Task EnsureAvailableAsync_ValidatesPolicyBeforeHttp(string source)
    {
        bool sent = false;
        using var client = new HttpClient(new Handler((_, _) => { sent = true; throw new InvalidOperationException("Unexpected HTTP call."); }));
        await Assert.ThrowsAnyAsync<Exception>(() => new ArtifactAvailabilityProbe(client)
            .EnsureAvailableAsync(Artifact(source), TestContext.Current.CancellationToken));
        Assert.False(sent);
    }

    [Fact]
    public async Task EnsureAvailableAsync_NormalizesOnlyScopedMicrosoftEsd()
    {
        Uri? requested = null;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) });
        }));
        await new ArtifactAvailabilityProbe(client).EnsureAvailableAsync(Artifact("https://dl.delivery.mp.microsoft.com/install.esd"), TestContext.Current.CancellationToken);
        Assert.Equal("http://dl.delivery.mp.microsoft.com/install.esd", requested?.AbsoluteUri);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureAvailableAsync_DeadlineCoversHeadersAndBody(bool waitForBody)
    {
        var body = new BlockingStream();
        using var client = new HttpClient(new Handler(async (_, token) =>
        {
            if (!waitForBody) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) };
        }));
        await Assert.ThrowsAsync<TimeoutException>(() => new ArtifactAvailabilityProbe(client, TimeSpan.FromMilliseconds(30))
            .EnsureAvailableAsync(Artifact(), TestContext.Current.CancellationToken));
        if (waitForBody) Assert.True(body.Disposed);
    }

    [Fact]
    public async Task EnsureAvailableAsync_PropagatesCallerCancellationDuringBodyRead()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var body = new BlockingStream();
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) })));
        Task operation = new ArtifactAvailabilityProbe(client).EnsureAvailableAsync(Artifact(), cancellation.Token);
        await body.ReadStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.True(body.Disposed);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.PartialContent)]
    public async Task EnsureAvailableAsync_ReadsOnlyOneByteAndDisposesBody(HttpStatusCode status)
    {
        var body = new TrackingStream([1, 2, 3]);
        using var client = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("bytes=0-0", request.Headers.Range?.ToString());
            var response = new HttpResponseMessage(status) { Content = new StreamContent(body) };
            if (status == HttpStatusCode.PartialContent) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, 3);
            return Task.FromResult(response);
        }));

        await new ArtifactAvailabilityProbe(client).EnsureAvailableAsync(Artifact(), TestContext.Current.CancellationToken);

        Assert.Equal(1, body.BytesRead);
        Assert.True(body.Disposed);
    }

    [Fact]
    public async Task EnsureAvailableAsync_RejectsEmptyBody()
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new ByteArrayContent([]) })));
        await Assert.ThrowsAsync<InvalidDataException>(() => new ArtifactAvailabilityProbe(client)
            .EnsureAvailableAsync(Artifact(), TestContext.Current.CancellationToken));
    }

    private static ArtifactIdentity Artifact(string url = "https://example.test/install.esd") => new(
        "revision", "source", new Uri(url), "install.esd",
        new FileIntegrity(new FileDigest(HashAlgorithmName.SHA256, new string('A', 64)), 3), "OperatingSystemImage", null);

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }
        public bool Disposed { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = base.Read(buffer.Span);
            BytesRead += read;
            return ValueTask.FromResult(read);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class BlockingStream : Stream
    {
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Assert.Equal(1, buffer.Length);
            ReadStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
