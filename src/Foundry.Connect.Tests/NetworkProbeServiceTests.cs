// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Http;
using System.Text;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Models.Network;
using Foundry.Connect.Services.Network;
using Foundry.Core.Models.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Connect.Tests;

public sealed class NetworkProbeServiceTests
{
    [Theory]
    [InlineData(200, "Microsoft Connect Test", NetworkReadinessStatus.Online)]
    [InlineData(302, "Microsoft Connect Test", NetworkReadinessStatus.UnexpectedResponse)]
    [InlineData(200, "<html>Sign in</html>", NetworkReadinessStatus.UnexpectedResponse)]
    [InlineData(200, "Microsoft Connect Test\n", NetworkReadinessStatus.UnexpectedResponse)]
    [InlineData(204, "", NetworkReadinessStatus.UnexpectedResponse)]
    [InlineData(407, "", NetworkReadinessStatus.ProxyAuthenticationRequired)]
    public async Task ProbeAsync_RequiresExactStatusAndBody(int status, string body, NetworkReadinessStatus expected)
    {
        using var service = CreateService(new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(body, Encoding.UTF8),
            Headers = { Location = new Uri("https://portal.test/login") }
        })));
        NetworkProbeResult result = await service.ProbeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProbeAsync_RejectsOversizedDeclaredOrStreamingBody(bool declared)
    {
        using var service = CreateService(new Handler((_, _) =>
        {
            HttpContent content = declared ? new StringContent(new string('x', 4097))
                : new StreamContent(new NonSeekableStream(new byte[4097]));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }));
        Assert.Equal(NetworkReadinessStatus.UnexpectedResponse, (await service.ProbeAsync(TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task ProbeAsync_DnsFailureHasDistinctResult()
    {
        using var service = CreateService(new Handler((_, _) => throw new HttpRequestException(HttpRequestError.NameResolutionError)));
        NetworkProbeResult result = await service.ProbeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(NetworkReadinessStatus.NameResolutionFailed, result.Status);
        Assert.Equal("network_dns_failed", result.ErrorCode);
    }

    [Fact]
    public async Task ProbeAsync_BodyReadHonorsRequestDeadline()
    {
        CancellationToken headersToken = default;
        CancellationToken bodyToken = default;
        using var service = CreateService(new Handler((_, token) =>
        {
            headersToken = token;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new BlockingStream(token => bodyToken = token))
            });
        }), new InternetProbeOptions { TimeoutSeconds = 1 });
        NetworkProbeResult result = await service.ProbeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(NetworkReadinessStatus.TimedOut, result.Status);
        Assert.True(headersToken.CanBeCanceled);
        Assert.True(bodyToken.IsCancellationRequested);
    }

    [Fact]
    public async Task ProbeAsync_CallerCancellationDuringBodyPropagates()
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var service = CreateService(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BlockingStream(_ => caller.Cancel()))
        })));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ProbeAsync(caller.Token));
    }

    [Theory]
    [InlineData("ftp://example.test", 200, "ok", 5)]
    [InlineData("/relative", 200, "ok", 5)]
    [InlineData("https://example.test", 302, "ok", 5)]
    [InlineData("https://example.test", 200, "", 5)]
    [InlineData("https://example.test", 200, "ok", 0)]
    [InlineData("https://example.test", 200, "ok", 31)]
    [InlineData("https://user:password@example.test", 200, "ok", 5)]
    public async Task ProbeAsync_InvalidExpectationDoesNotSendRequest(string uri, int status, string body, int timeout)
    {
        using var service = CreateService(new Handler((_, _) => throw new InvalidOperationException("No request expected.")),
            new InternetProbeOptions { Probes = [new(uri, status, body)], TimeoutSeconds = timeout });
        NetworkProbeResult result = await service.ProbeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(NetworkReadinessStatus.Unavailable, result.Status);
        Assert.Equal("network_probe_configuration_invalid", result.ErrorCode);
    }

    [Fact]
    public async Task ProbeAsync_UnknownLegacyEndpointCannotBecomeReady()
    {
        using var service = CreateService(new Handler((_, _) => throw new InvalidOperationException("No request expected.")),
            new InternetProbeOptions { ProbeUris = ["https://example.test/custom?token=secret"] });
        Assert.Equal(NetworkReadinessStatus.Unavailable, (await service.ProbeAsync(TestContext.Current.CancellationToken)).Status);
    }

    private static NetworkProbeService CreateService(HttpMessageHandler handler, InternetProbeOptions? options = null) =>
        new(options ?? new(), new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, NullLogger<NetworkProbeService>.Instance);

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private sealed class BlockingStream(Action<CancellationToken> observe) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            observe(cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
