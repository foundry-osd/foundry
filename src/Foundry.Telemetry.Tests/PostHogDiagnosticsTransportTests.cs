// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Net;
using Serilog.Events;

namespace Foundry.Telemetry.Tests;

public sealed class PostHogDiagnosticsTransportTests
{
    [Fact]
    public async Task Exporter_ExportsBothLogsAndExceptionsThroughInjectedHttp()
    {
        var requests = new ConcurrentBag<string>();
        using var generation = new TelemetryConsentGeneration();
        await using var exporter = Create(generation, requests);
        await exporter.ExportAsync(Record(), TestContext.Current.CancellationToken);
        await exporter.FlushAsync(TestContext.Current.CancellationToken);
        Assert.Contains("logs", requests);
        Assert.Contains("exceptions", requests);
    }

    [Fact]
    public async Task Exporter_RevokedGenerationCannotSendFromFlushOrDisposal()
    {
        var requests = new ConcurrentBag<string>();
        using var generation = new TelemetryConsentGeneration();
        var exporter = Create(generation, requests);
        generation.Revoke();
        await exporter.ExportAsync(Record(), TestContext.Current.CancellationToken);
        await exporter.FlushAsync(TestContext.Current.CancellationToken);
        await exporter.DisposeAsync();
        Assert.Empty(requests);
    }

    [Fact]
    public async Task Exporter_QueuedOldGenerationRemainsClosedAfterFreshGenerationSends()
    {
        var requests = new ConcurrentBag<string>();
        using var oldGeneration = new TelemetryConsentGeneration();
        var old = Create(oldGeneration, requests);
        await old.ExportAsync(Record(), TestContext.Current.CancellationToken);
        oldGeneration.Revoke();
        await oldGeneration.WaitForDrainAsync(TestContext.Current.CancellationToken);
        int admitted = requests.Count;
        await old.FlushAsync(TestContext.Current.CancellationToken);
        await old.DisposeAsync();
        Assert.Equal(admitted, requests.Count);
        requests.Clear();
        using var fresh = new TelemetryConsentGeneration();
        await using var current = Create(fresh, requests);
        await current.ExportAsync(Record(), TestContext.Current.CancellationToken);
        await current.FlushAsync(TestContext.Current.CancellationToken);
        Assert.Contains("logs", requests);
        Assert.Contains("exceptions", requests);
    }

    private static PostHogDiagnosticsExporter Create(TelemetryConsentGeneration generation, ConcurrentBag<string> requests)
        => new(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context(), generation, channel => new Handler(channel, requests));

    private static RemoteDiagnosticRecord Record() => RemoteDiagnosticPropertyPolicy.CreateSanitizedRecord(
        RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "Synthetic failure", new InvalidOperationException("Synthetic detail")), RemoteDiagnosticsTestData.Context());

    private sealed class Handler(string channel, ConcurrentBag<string> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(channel);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = channel == "exceptions" ? new StringContent("{\"status\":1}") : new ByteArrayContent([]) });
        }
    }
}
