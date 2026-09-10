// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Foundry.Telemetry;
using Serilog.Events;

namespace Foundry.Telemetry.Tests;

public sealed class BootstrapTelemetryPipelineTests
{
    private static readonly TelemetryContext Context = new(TelemetryApps.FoundryBootstrap, "1.2.3", "release", "winpe",
        "release", "usb", "x64", "en-US", "boot-session");

    [Theory]
    [InlineData(false, false, 0, 0)]
    [InlineData(false, true, 0, 2)]
    [InlineData(true, false, 1, 0)]
    [InlineData(true, true, 1, 2)]
    public async Task Capture_RespectsIndependentConsent(bool usage, bool diagnostics, int analyticsCount, int diagnosticCount)
    {
        var transport = new RecordingTransport();
        await using var pipeline = Create(transport, usage: usage, diagnostics: diagnostics);
        pipeline.Emit(Event(LogEventLevel.Error, new InvalidOperationException("password=secret")));
        pipeline.CaptureTerminalFailure(Failure());
        pipeline.CaptureTerminalFailure(Failure());
        Assert.Empty(transport.Records);
        Assert.Equal(analyticsCount, pipeline.PendingRecords.Count(record => record.Destination == BootstrapTelemetryDestination.Analytics));
        Assert.Equal(diagnosticCount, pipeline.PendingRecords.Count(record => record.Destination != BootstrapTelemetryDestination.Analytics));
        await pipeline.ShutdownAsync(TestContext.Current.CancellationToken);
        Assert.Equal(analyticsCount + diagnosticCount, transport.Records.Count);
    }

    [Fact]
    public async Task Logs_KeepFilterRateLimitAndExceptionDedupe()
    {
        var transport = new RecordingTransport();
        await using var pipeline = Create(transport);
        pipeline.Emit(Event(LogEventLevel.Debug));
        pipeline.Emit(Event(LogEventLevel.Information));
        pipeline.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Information, "Milestone", null, ("RemoteDiagnostic", true)));
        var exception = new IOException("password=secret");
        for (int index = 0; index < 20; index++) pipeline.Emit(Event(LogEventLevel.Error, exception));
        Assert.Equal(6, pipeline.PendingRecords.Count(record => record.Destination == BootstrapTelemetryDestination.Log));
        Assert.Single(pipeline.PendingRecords, record => record.Destination == BootstrapTelemetryDestination.Exception);
        Assert.DoesNotContain(pipeline.PendingRecords, record => record.Destination == BootstrapTelemetryDestination.Analytics);
    }

    [Fact]
    public async Task Delivery_DrainsBeforePreparationRecordsAndContinuesAcceptingNewRecords()
    {
        var transport = new RecordingTransport();
        await using var pipeline = Create(transport);
        pipeline.Emit(Event(LogEventLevel.Warning));
        Assert.Empty(transport.Records);
        pipeline.StartDelivery(true);
        await transport.WaitForCountAsync(1);
        pipeline.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Warning, "Later warning"));
        await transport.WaitForCountAsync(2);
        Assert.Equal(0, transport.FlushCount);
        await pipeline.ShutdownAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, transport.FlushCount);
    }

    [Fact]
    public async Task ProcessExitObservation_DoesNotManufactureAnException()
    {
        await using var pipeline = Create(new RecordingTransport());
        pipeline.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "Child exited", null, ("ExitCode", 22)));
        Assert.Single(pipeline.PendingRecords);
        Assert.Null(pipeline.PendingRecords[0].Diagnostic!.Exception);
    }

    [Fact]
    public async Task Journal_RetainsIdentityTimestampAndCapsUnacknowledgedHandoffsAcrossBoots()
    {
        using var folder = new TestFolder();
        Guid identity;
        DateTimeOffset timestamp;
        var first = new RecordingTransport();
        await using (var pipeline = Create(first, folder.Path))
        {
            pipeline.Emit(Event(LogEventLevel.Warning));
            identity = pipeline.PendingRecords[0].Id;
            timestamp = pipeline.PendingRecords[0].Timestamp;
            await pipeline.ShutdownAsync(TestContext.Current.CancellationToken);
            Assert.Equal(BootstrapDeliveryState.HandedToTransport, pipeline.PendingRecords[0].State);
        }
        for (int boot = 2; boot <= 4; boot++)
        {
            var transport = new RecordingTransport();
            await using var pipeline = Create(transport, folder.Path);
            pipeline.StartDelivery(true);
            await pipeline.ShutdownAsync(TestContext.Current.CancellationToken);
            Assert.Equal(boot <= 3 ? 1 : 0, transport.Records.Count);
            Assert.Equal(identity, pipeline.PendingRecords[0].Id);
            Assert.Equal(timestamp, pipeline.PendingRecords[0].Timestamp);
            Assert.Equal(Math.Min(boot, 3), pipeline.PendingRecords[0].Attempts);
        }
    }

    [Fact]
    public async Task PartialReceipt_RemovesOnlyAcknowledgedDestination()
    {
        using var folder = new TestFolder();
        var transport = new RecordingTransport { Acknowledge = record => record.Destination == BootstrapTelemetryDestination.Log };
        await using (var pipeline = Create(transport, folder.Path))
        {
            pipeline.Emit(Event(LogEventLevel.Error, new IOException("Failed")));
            await pipeline.ShutdownAsync(TestContext.Current.CancellationToken);
            Assert.Equal(BootstrapTelemetryDestination.Exception, Assert.Single(pipeline.PendingRecords).Destination);
        }
        var next = new RecordingTransport();
        await using var replay = Create(next, folder.Path);
        await replay.ShutdownAsync(TestContext.Current.CancellationToken);
        Assert.Equal(BootstrapTelemetryDestination.Exception, Assert.Single(next.Records).Destination);
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 2)]
    [InlineData(true, true, 3)]
    public async Task RestrictionBeforeReplay_PurgesEachCategoryIndependently(bool usage, bool diagnostics, int expected)
    {
        using var folder = new TestFolder();
        await using (var first = Create(new RecordingTransport(), folder.Path))
        {
            first.CaptureTerminalFailure(Failure());
            first.Emit(Event(LogEventLevel.Error, new IOException("Failed")));
            await first.ShutdownAsync(new CancellationToken(true));
        }
        var transport = new RecordingTransport();
        await using var pipeline = Create(transport, folder.Path);
        pipeline.RestrictConsent(usage, diagnostics);
        Assert.Equal(expected, pipeline.PendingRecords.Count);
        pipeline.StartDelivery(true);
        await pipeline.ShutdownAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expected, transport.Records.Count);
        Assert.Equal(expected, File.ReadAllLines(folder.Path).Length);
    }

    [Fact]
    public async Task ChangedInstallationOrDestination_DoesNotReplayOldRecords()
    {
        using var folder = new TestFolder();
        await using (var first = Create(new RecordingTransport(), folder.Path))
        {
            first.CaptureTerminalFailure(Failure());
            first.Emit(Event(LogEventLevel.Warning));
        }
        var transport = new RecordingTransport();
        await using var changed = Create(transport, folder.Path, installation: "another-installation");
        await changed.ShutdownAsync(TestContext.Current.CancellationToken);
        Assert.Empty(transport.Records);
        Assert.Empty(changed.PendingRecords);
    }

    [Fact]
    public async Task ClockGate_ExpiresOldRecordsOnlyAfterUsableClock()
    {
        using var folder = new TestFolder();
        var clock = new TestClock();
        await using (var first = Create(new RecordingTransport(), folder.Path, time: clock))
            first.CaptureTerminalFailure(Failure());
        clock.UtcNow += TimeSpan.FromDays(8);
        await using var replay = Create(new RecordingTransport(), folder.Path, time: clock);
        Assert.Single(replay.PendingRecords);
        replay.StartDelivery(true);
        Assert.Empty(replay.PendingRecords);
    }

    [Fact]
    public async Task ClockCorrection_DoesNotExpireRecordsCapturedDuringCurrentBoot()
    {
        var clock = new TestClock();
        var transport = new RecordingTransport();
        await using var pipeline = Create(transport, time: clock);
        pipeline.CaptureTerminalFailure(Failure());
        DateTimeOffset original = pipeline.PendingRecords[0].Timestamp;
        clock.UtcNow += TimeSpan.FromDays(365);
        pipeline.StartDelivery(true);
        await pipeline.ShutdownAsync(TestContext.Current.CancellationToken);
        Assert.Equal(original, Assert.Single(transport.Records).Timestamp);
    }

    [Fact]
    public async Task DurableAttemptWriteFailure_DoesNotSubmitUnrecordedAttempts()
    {
        using var folder = new TestFolder();
        Directory.CreateDirectory(folder.Path);
        var transport = new RecordingTransport();
        await using var pipeline = Create(transport, folder.Path);
        pipeline.CaptureTerminalFailure(Failure());
        await pipeline.ShutdownAsync(TestContext.Current.CancellationToken);
        Assert.Empty(transport.Records);
        Assert.Single(pipeline.PendingRecords);
    }

    [Fact]
    public async Task Replay_IsLimitedToOneHundredRecordsPerBoot()
    {
        using var folder = new TestFolder();
        await using (var first = Create(new RecordingTransport(), folder.Path))
        {
            for (int index = 0; index < 110; index++)
                first.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Warning, "Warning " + index));
        }
        var transport = new RecordingTransport();
        await using var replay = Create(transport, folder.Path, time: new TestClock());
        replay.StartDelivery(clockUsable: false);
        await transport.WaitForCountAsync(100, TimeSpan.FromSeconds(30));
        await replay.ShutdownAsync(TestContext.Current.CancellationToken);
        Assert.Equal(100, transport.Records.Count);
    }

    [Fact]
    public async Task Replay_UsesSharedMonotonicFiveSecondBudget()
    {
        using var folder = new TestFolder();
        var clock = new TestClock();
        await using (var first = Create(new RecordingTransport(), folder.Path, time: clock))
        {
            first.Emit(Event(LogEventLevel.Warning));
            first.CaptureTerminalFailure(Failure());
        }
        var transport = new RecordingTransport
        {
            Acknowledge = _ => { clock.Timestamp += 6 * clock.TimestampFrequency; return false; }
        };
        await using var replay = Create(transport, folder.Path, time: clock);
        await replay.ShutdownAsync(TestContext.Current.CancellationToken);
        Assert.Single(transport.Records);
    }

    [Fact]
    public async Task Shutdown_OutageSharesTwoSecondBudget()
    {
        var transport = new RecordingTransport { Block = true };
        await using var pipeline = Create(transport);
        pipeline.CaptureTerminalFailure(Failure());
        var elapsed = Stopwatch.StartNew();
        await pipeline.ShutdownAsync(TestContext.Current.CancellationToken);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(3));
        Assert.Single(pipeline.PendingRecords);
    }

    [Fact]
    public async Task CorruptAndUntrustedJournal_IsBoundedAndResanitized()
    {
        using var folder = new TestFolder();
        await using (var first = Create(new RecordingTransport(), folder.Path))
        {
            first.CaptureTerminalFailure(Failure());
            first.Emit(Event(LogEventLevel.Warning));
        }
        string[] lines = File.ReadAllLines(folder.Path);
        var record = JsonSerializer.Deserialize<BootstrapPendingRecord>(lines[0])!;
        record.Properties!["password"] = "private-secret";
        record.Properties["last_stage"] = "password=private-secret";
        File.WriteAllLines(folder.Path, ["not json", JsonSerializer.Serialize(record), lines[1]]);
        var transport = new RecordingTransport();
        await using var replay = Create(transport, folder.Path);
        await replay.ShutdownAsync(TestContext.Current.CancellationToken);
        string content = File.ReadAllText(folder.Path);
        Assert.DoesNotContain("private-secret", content, StringComparison.Ordinal);
        Assert.DoesNotContain("not json", content, StringComparison.Ordinal);
        Assert.Equal(2, transport.Records.Count);
    }

    [Fact]
    public async Task InvalidJournalNumber_DoesNotDiscardOtherValidRecords()
    {
        using var folder = new TestFolder();
        await using (var first = Create(new RecordingTransport(), folder.Path))
        {
            first.CaptureTerminalFailure(Failure());
            first.Emit(Event(LogEventLevel.Warning));
        }
        string[] lines = File.ReadAllLines(folder.Path);
        string invalid = lines[1].Replace("\"Attributes\":{", "\"Attributes\":{\"duration.ms\":1e999,", StringComparison.Ordinal);
        File.WriteAllLines(folder.Path, [invalid, lines[0]]);
        var transport = new RecordingTransport();
        await using var replay = Create(transport, folder.Path);
        await replay.ShutdownAsync(TestContext.Current.CancellationToken);
        Assert.Equal(BootstrapTelemetryDestination.Analytics, Assert.Single(transport.Records).Destination);
    }

    [Fact]
    public void Journal_EnforcesByteLimitWithoutClock()
    {
        using var folder = new TestFolder();
        var journal = new BootstrapTelemetryJournal(folder.Path);
        for (int index = 0; index < 30; index++)
            journal.Add(new BootstrapPendingRecord(Guid.NewGuid(), new string('a', 64), BootstrapTelemetryDestination.Analytics,
                DateTimeOffset.MinValue, new Dictionary<string, object> { ["data"] = new string('a', 200000) }, null));
        Assert.True(new FileInfo(folder.Path).Length <= BootstrapTelemetryJournal.MaximumBytes);
        Assert.True(journal.Records.Count < 30);
    }

    [Fact]
    public void FailureProperties_RejectUnknownCategoriesAndUnboundedValues()
    {
        var properties = new Dictionary<string, object?>
        {
            ["failure_category"] = "private-name",
            ["last_stage"] = "private-name",
            ["child_application"] = "private-name",
            ["payload_source"] = "private-name",
            ["architecture"] = "private-name",
            ["payload_version"] = "private-name",
            ["elapsed_seconds"] = double.PositiveInfinity
        };
        Assert.Empty(TelemetryEventPropertyPolicy.Sanitize(TelemetryEvents.BootstrapFailed, properties));
        var valid = TelemetryEventPropertyPolicy.Sanitize(TelemetryEvents.BootstrapFailed, Failure());
        Assert.Equal("foundry_connect", valid["child_application"]);
        Assert.Equal("child_exit", valid["failure_category"]);
    }

    [Fact]
    public async Task AnalyticsTransport_UsesOriginalUuidAndTimestampAndRequiresHttpAcceptance()
    {
        var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var service = new PostHogTelemetryService(client, new TelemetryOptions(true, "https://example.test", "public", "install"), Context);
        Guid id = Guid.NewGuid();
        DateTimeOffset timestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var properties = PostHogTelemetryService.BuildProperties(Context, TelemetryEvents.BootstrapFailed, Failure());
        Assert.False(await service.SendDurableAsync(id, timestamp, properties, CancellationToken.None));
        handler.Status = HttpStatusCode.OK;
        Assert.True(await service.SendDurableAsync(id, timestamp, properties, CancellationToken.None));
        Assert.Equal(handler.Payloads[0], handler.Payloads[1]);
        using var payload = JsonDocument.Parse(handler.Payloads[0]);
        Assert.Equal(id, payload.RootElement.GetProperty("uuid").GetGuid());
        Assert.Equal(timestamp, payload.RootElement.GetProperty("timestamp").GetDateTimeOffset());
    }

    private static BootstrapTelemetryPipeline Create(RecordingTransport transport, string? path = null,
        bool usage = true, bool diagnostics = true, string installation = "install", TimeProvider? time = null) =>
        new(new TelemetryOptions(usage, "https://example.test", "public", installation), Context,
            new RemoteDiagnosticsOptions(diagnostics, "https://example.test", "public", installation),
            TelemetryContextFactory.CreateRemoteDiagnosticsContext(Context), path, () => transport, time ?? TimeProvider.System);

    private static LogEvent Event(LogEventLevel level, Exception? exception = null) =>
        RemoteDiagnosticsTestData.LogEvent(level, "Bootstrap operation failed", exception);

    private static Dictionary<string, object?> Failure() => new()
    {
        ["failure_category"] = "child_exit",
        ["last_stage"] = "connect",
        ["elapsed_seconds"] = 1.5,
        ["child_application"] = "foundry_connect",
        ["child_exit_code"] = 22,
        ["password"] = "private-secret"
    };

    private sealed class RecordingTransport : IBootstrapTelemetryTransport
    {
        internal ConcurrentQueue<BootstrapPendingRecord> Records { get; } = new();
        internal Func<BootstrapPendingRecord, bool> Acknowledge { get; init; } = _ => false;
        internal bool Block { get; init; }
        internal int FlushCount { get; private set; }
        public async Task<bool> DeliverAsync(BootstrapPendingRecord record, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Records.Enqueue(record);
            if (Block) await Task.Delay(Timeout.Infinite, cancellationToken);
            return Acknowledge(record);
        }
        public Task FlushAsync(CancellationToken cancellationToken) { FlushCount++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        internal async Task WaitForCountAsync(int count, TimeSpan? waitTimeout = null)
        {
            using var timeout = new CancellationTokenSource(waitTimeout ?? TimeSpan.FromSeconds(3));
            while (Records.Count < count) await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class TestClock : TimeProvider
    {
        internal DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        internal long Timestamp { get; set; }
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public override long GetTimestamp() => Timestamp;
    }

    private sealed class TestFolder : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        internal TestFolder() => Directory.CreateDirectory(_directory);
        internal string Path => System.IO.Path.Combine(_directory, "pending.jsonl");
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        internal HttpStatusCode Status { get; set; } = HttpStatusCode.ServiceUnavailable;
        internal List<string> Payloads { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Payloads.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(Status);
        }
    }
}
