// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Http.Headers;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Http;

namespace Foundry.Deploy.Tests;

public sealed class ImageSourceProbeTests
{
    [Fact]
    public async Task ProbeAsync_TlsFailure_PreservesActionableGuidanceAndCause()
    {
        var failure = new HttpRequestException(HttpRequestError.SecureConnectionError, "Handshake failed");
        using var client = new HttpClient(new Handler((_, _) => throw failure));
        DeploymentOperationException exception = await Assert.ThrowsAsync<DeploymentOperationException>(() =>
            new ImageSourceProbe(client).ProbeAsync("https://example.test/image", TestContext.Current.CancellationToken));
        Assert.Equal(HttpConnectionFailure.SecureConnectionMessage, exception.Message);
        Assert.Same(failure, exception.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProbeAsync_UsesGetRangeAndDisposesWithoutReadingBody(bool honorsRange)
    {
        var content = new UnreadableContent();
        content.Headers.ContentLength = honorsRange ? 1 : 1234;
        if (honorsRange) content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, 1234);
        using var client = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("bytes=0-0", request.Headers.Range?.ToString());
            return Task.FromResult(new HttpResponseMessage(honorsRange ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = content });
        }));

        long? length = await new ImageSourceProbe(client).ProbeAsync("https://example.test/install.esd", TestContext.Current.CancellationToken);

        Assert.Equal(1234, length);
        Assert.True(content.WasDisposed);
    }

    [Fact]
    public async Task ProbeAsync_MissingLength_IsUnknownAndStillChecksAccess()
    {
        int calls = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnreadableContent() });
        }));
        Assert.Null(await new ImageSourceProbe(client).ProbeAsync("https://example.test/image", TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task ProbeAsync_UnavailableResponse_Fails(HttpStatusCode status)
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status))));
        await Assert.ThrowsAsync<DeploymentOperationException>(() => new ImageSourceProbe(client)
            .ProbeAsync("https://example.test/image", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProbeAsync_WindowsUpdateDelivery_UsesExistingNormalizationPolicy()
    {
        Uri? source = null;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            source = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnreadableContent() });
        }));
        await new ImageSourceProbe(client).ProbeAsync("https://dl.delivery.mp.microsoft.com/image", TestContext.Current.CancellationToken);
        Assert.Equal("http", source?.Scheme);
    }

    [Fact]
    public async Task ProbeAsync_CallerCancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var client = new HttpClient(new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ImageSourceProbe(client).ProbeAsync("https://example.test/image", cancellation.Token));
    }

    [Fact]
    public async Task ProbeAsync_StalledHeaders_HasBoundedDeadline()
    {
        using var client = new HttpClient(new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        DeploymentOperationException exception = await Assert.ThrowsAsync<DeploymentOperationException>(() =>
            new ImageSourceProbe(client, TimeSpan.FromMilliseconds(30)).ProbeAsync("https://example.test/image", TestContext.Current.CancellationToken));
        Assert.Equal(DeploymentFailureReasons.DeadlineExceeded, exception.Failure.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProbeAsync_InvalidLengthOrRange_Fails(bool invalidRange)
    {
        var content = new UnreadableContent();
        content.Headers.ContentLength = invalidRange ? 1 : 0;
        if (invalidRange) content.Headers.ContentRange = new ContentRangeHeaderValue(1, 1, 1234);
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(
            new HttpResponseMessage(invalidRange ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = content })));
        await Assert.ThrowsAsync<DeploymentOperationException>(() => new ImageSourceProbe(client).ProbeAsync("https://example.test/image", TestContext.Current.CancellationToken));
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class UnreadableContent : HttpContent
    {
        public bool WasDisposed { get; private set; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new InvalidOperationException("The probe must never read the body.");
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
}
