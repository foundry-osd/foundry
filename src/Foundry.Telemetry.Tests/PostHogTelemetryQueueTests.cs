// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace Foundry.Telemetry.Tests;

public sealed class PostHogTelemetryQueueTests
{
    [Fact]
    public async Task TrackAsync_CompletesWhileTransportIsBlocked()
    {
        var handler = new Handler { Block = true };
        await using var service = Create(_ => handler);
        Task track = service.TrackAsync(TelemetryEvents.OsdBootMediaFinished, new Dictionary<string, object?>());
        await handler.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        try { Assert.True(track.IsCompletedSuccessfully); }
        finally { handler.Release.TrySetResult(); await track; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetEnabled_ChangesExistingServiceWithoutChangingInstallIdentity(bool initiallyEnabled)
    {
        var handlers = new List<Handler>();
        await using var service = Create(_ => { var handler = new Handler(); handlers.Add(handler); return handler; }, initiallyEnabled);
        service.SetEnabled(true);
        await service.TrackAsync(TelemetryEvents.OsdBootMediaFinished, new Dictionary<string, object?>());
        await service.FlushAsync(TestContext.Current.CancellationToken);
        service.SetEnabled(false);
        await service.TrackAsync(TelemetryEvents.OsdBootMediaFinished, new Dictionary<string, object?>());
        service.SetEnabled(true);
        await service.TrackAsync(TelemetryEvents.OsdBootMediaFinished, new Dictionary<string, object?>());
        await service.FlushAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, handlers.Count);
        string[] bodies = handlers.SelectMany(handler => handler.Bodies).ToArray();
        Assert.Equal(2, bodies.Length);
        Assert.All(bodies, body => Assert.Equal("synthetic-install", JsonDocument.Parse(body).RootElement.GetProperty("distinct_id").GetString()));
    }

    [Fact]
    public async Task Queue_DropsNewEventsAfter64AndSnapshotsCallerProperties()
    {
        var handler = new Handler { Block = true };
        await using var service = Create(_ => handler);
        await service.TrackAsync(TelemetryEvents.OsdBootMediaFinished, new Dictionary<string, object?>());
        await handler.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var properties = new Dictionary<string, object?> { ["boot_media_target"] = "iso" };
        for (int i = 0; i < 100; i++)
        {
            Task local = service.TrackAsync(TelemetryEvents.OsdBootMediaFinished, properties);
            Assert.True(local.IsCompletedSuccessfully);
        }
        properties["boot_media_target"] = "usb";
        handler.Release.TrySetResult();
        await service.FlushAsync(TestContext.Current.CancellationToken);
        Assert.Equal(65, handler.Bodies.Count);
        foreach (string body in handler.Bodies.Skip(1))
        {
            using JsonDocument json = JsonDocument.Parse(body);
            Assert.Equal("iso", json.RootElement.GetProperty("properties").GetProperty("boot_media_target").GetString());
        }
    }

    [Fact]
    public async Task Disable_DropsOldQueueAndFreshGenerationSendsOnlyNewData()
    {
        var old = new Handler { Block = true };
        var fresh = new Handler();
        int factories = 0;
        await using var service = Create(_ => ++factories == 1 ? old : fresh);
        await service.TrackAsync(TelemetryEvents.OsdBootMediaFinished, new Dictionary<string, object?>());
        await old.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        await service.TrackAsync(TelemetryEvents.OsdBootMediaFinished, new Dictionary<string, object?> { ["boot_media_target"] = "iso" });
        service.SetEnabled(false);
        await old.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        service.SetEnabled(true);
        await service.TrackAsync(TelemetryEvents.OsdBootMediaFinished, new Dictionary<string, object?> { ["boot_media_target"] = "usb" });
        await service.FlushAsync(TestContext.Current.CancellationToken);
        Assert.Empty(old.Bodies);
        using JsonDocument json = JsonDocument.Parse(Assert.Single(fresh.Bodies));
        Assert.Equal("usb", json.RootElement.GetProperty("properties").GetProperty("boot_media_target").GetString());
        await service.DisposeAsync();
        Assert.Equal(1, old.DisposeCount);
        Assert.Equal(1, fresh.DisposeCount);
    }

    [Fact]
    public async Task CancelledFlush_RevokesAdmittedRequestAndReturnsWithoutFailure()
    {
        var handler = new Handler { Block = true };
        await using var service = Create(_ => handler);
        await service.TrackAsync(TelemetryEvents.OsdBootMediaFinished, new Dictionary<string, object?>());
        await handler.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await service.FlushAsync(cancelled.Token).WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        await handler.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task InvalidConfiguration_CannotBeEnabled()
    {
        int factories = 0;
        await using var service = new PostHogTelemetryService(_ => { factories++; throw new InvalidOperationException(); },
            new(false, "not-a-host", "token", "install"), Context());
        service.SetEnabled(true);
        await service.TrackAsync(TelemetryEvents.OsdBootMediaFinished, new Dictionary<string, object?>());
        Assert.Equal(0, factories);
    }

    [Fact]
    public async Task CancelledFlush_DisposalDoesNotStartAnotherWaitForUncooperativeTransport()
    {
        var handler = new Handler { Block = true, IgnoreCancellation = true };
        var service = Create(_ => handler);
        await service.TrackAsync(TelemetryEvents.OsdBootMediaFinished, new Dictionary<string, object?>(), TestContext.Current.CancellationToken);
        await handler.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await service.FlushAsync(cancelled.Token);
            await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
            Assert.Equal(0, handler.DisposeCount);
        }
        finally
        {
            handler.Release.TrySetResult();
            await handler.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            await service.DisposeAsync();
        }
        Assert.Equal(1, handler.DisposeCount);
    }

    private static PostHogTelemetryService Create(Func<TelemetryConsentGeneration, HttpMessageHandler> factory, bool enabled = true) =>
        new(generation => new HttpClient(new ConsentHttpMessageHandler(generation, factory(generation))),
            new(enabled, "https://example.com", "synthetic-token", "synthetic-install"),
            Context());

    private static TelemetryContext Context() => new(TelemetryApps.FoundryOsd, "1", "debug", TelemetryRuntimeModes.Desktop,
        TelemetryRuntimePayloadSources.None, TelemetryBootMediaTargets.Iso, "x64", "en-US", "synthetic-session");

    private sealed class Handler : HttpMessageHandler
    {
        public bool Block { get; init; }
        public bool IgnoreCancellation { get; init; }
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCount { get; private set; }
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<string> Bodies { get; } = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            try
            {
                if (Block) await Release.Task.WaitAsync(IgnoreCancellation ? CancellationToken.None : cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }
            Bodies.Enqueue(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return new(HttpStatusCode.OK);
        }
        protected override void Dispose(bool disposing) { if (disposing) { DisposeCount++; Disposed.TrySetResult(); } base.Dispose(disposing); }
    }
}
