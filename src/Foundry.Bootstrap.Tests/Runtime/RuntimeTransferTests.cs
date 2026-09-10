// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using Foundry.Bootstrap.Runtime;
using Xunit;

namespace Foundry.Bootstrap.Tests.Runtime;

public sealed class RuntimeTransferTests
{
    [Fact]
    public async Task RequestTimeoutExhaustsRetriesWithoutCancellingCaller()
    {
        string destination = Path.GetTempFileName();
        try
        {
            using var caller = new CancellationTokenSource();
            using var handler = new TimeoutHandler();
            using var client = new HttpClient(handler);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RuntimeTransfer(client, null)
                .SaveAsync("https://example.test/payload.zip", destination, "Foundry.Connect", caller.Token));
            Assert.False(caller.IsCancellationRequested);
            Assert.Equal(3, handler.Requests);
        }
        finally { File.Delete(destination); }
    }

    [Fact]
    public async Task CallerCancellationDuringRequestDoesNotRetry()
    {
        string destination = Path.GetTempFileName();
        try
        {
            using var caller = new CancellationTokenSource();
            using var handler = new TimeoutHandler(caller);
            using var client = new HttpClient(handler);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RuntimeTransfer(client, null)
                .SaveAsync("https://example.test/payload.zip", destination, "Foundry.Connect", caller.Token));
            Assert.True(caller.IsCancellationRequested);
            Assert.Equal(1, handler.Requests);
        }
        finally { File.Delete(destination); }
    }

    [Fact]
    public async Task InvalidTlsConfigurationDoesNotRetry()
    {
        string destination = Path.GetTempFileName();
        try
        {
            using var client = new HttpClient(new TlsFailureHandler());
            await Assert.ThrowsAsync<HttpRequestException>(() => new RuntimeTransfer(client, null)
                .SaveAsync("https://example.test/payload.zip", destination, "Foundry.Connect", TestContext.Current.CancellationToken));
            Assert.Empty(await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        }
        finally { File.Delete(destination); }
    }

    [Fact]
    public async Task InterruptedHttpBodyRetriesAndReplacesPartialDownload()
    {
        string destination = Path.GetTempFileName();
        try
        {
            using var client = new HttpClient(new InterruptedBodyHandler());
            var progress = new List<RuntimeDownloadProgress>();
            await new RuntimeTransfer(client, progress.Add).SaveAsync("https://example.test/payload.zip", destination, "Foundry.Connect", TestContext.Current.CancellationToken);
            Assert.Equal("complete", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
            Assert.Equal(8, progress[^1].BytesReceived);
            Assert.Equal(8, progress[^1].TotalBytes);
        }
        finally { File.Delete(destination); }
    }

    private sealed class InterruptedBodyHandler : HttpMessageHandler
    {
        private int requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = ++requests == 1 ? new StreamContent(new InterruptedStream()) : new StringContent("complete")
            });
        }
    }

    private sealed class TlsFailureHandler : HttpMessageHandler
    {
        private int requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (++requests == 1) throw new HttpRequestException(HttpRequestError.SecureConnectionError, "Certificate validation failed.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("unexpected retry") });
        }
    }

    private sealed class TimeoutHandler(CancellationTokenSource? caller = null) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            caller?.Cancel();
            return Task.FromException<HttpResponseMessage>(new TaskCanceledException("Request deadline exceeded."));
        }
    }

    private sealed class InterruptedStream : MemoryStream
    {
        private bool returnedPartial;
        public override bool CanSeek => false;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (returnedPartial) return ValueTask.FromException<int>(new HttpIOException(HttpRequestError.ResponseEnded, "Connection ended while receiving the payload."));
            returnedPartial = true;
            "bad"u8.CopyTo(buffer.Span);
            return ValueTask.FromResult(3);
        }
    }
}
