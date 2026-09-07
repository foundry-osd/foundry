// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;

namespace Foundry.Telemetry.Tests;

public sealed class ConsentHttpMessageHandlerTests
{
    [Fact]
    public async Task RevokedGeneration_ReturnsLocalNoContentWithoutTransport()
    {
        using var generation = new TelemetryConsentGeneration();
        var inner = new Handler();
        using var client = new HttpClient(new ConsentHttpMessageHandler(generation, inner));
        generation.Revoke();
        using HttpResponseMessage response = await client.GetAsync("https://example.com", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, inner.Calls);
    }

    [Fact]
    public async Task Revocation_CancelsAdmittedSendAndDrainWaitsForActualCompletion()
    {
        using var generation = new TelemetryConsentGeneration();
        var inner = new Handler { Block = true };
        using var client = new HttpClient(new ConsentHttpMessageHandler(generation, inner));
        Task<HttpResponseMessage> request = client.GetAsync("https://example.com", TestContext.Current.CancellationToken);
        await inner.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.False(generation.WaitForDrainAsync().IsCompleted);
            generation.Revoke();
            await inner.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        }
        finally
        {
            inner.Release.TrySetResult();
            try { (await request).Dispose(); } catch (OperationCanceledException) { }
        }
        await generation.WaitForDrainAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RevocationCallbacks_CanReenterAdmissionWithoutDeadlock()
    {
        using var generation = new TelemetryConsentGeneration();
        Assert.True(generation.TryBeginSend(out CancellationToken revoked));
        var callback = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = revoked.Register(() => callback.TrySetResult(generation.TryBeginSend(out _)));
        generation.Revoke();
        generation.EndSend();
        Assert.False(await callback.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Deadline_DoesNotDeclareUncooperativeTransportDrained()
    {
        using var generation = new TelemetryConsentGeneration();
        var inner = new Handler { Block = true };
        using var client = new HttpClient(new ConsentHttpMessageHandler(generation, inner));
        Task<HttpResponseMessage> request = client.GetAsync("https://example.com", TestContext.Current.CancellationToken);
        await inner.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken));
            Assert.False(generation.WaitForDrainAsync().IsCompleted);
        }
        finally
        {
            inner.Release.TrySetResult();
            await generation.WaitForDrainAsync(TestContext.Current.CancellationToken);
        }
    }

    private sealed class Handler : HttpMessageHandler
    {
        public bool Block { get; init; }
        public int Calls { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            using var registration = cancellationToken.Register(() => Cancelled.TrySetResult());
            Entered.TrySetResult();
            if (Block) await Release.Task.ConfigureAwait(false);
            return new(HttpStatusCode.OK);
        }
    }
}
