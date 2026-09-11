// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Events;
using Foundry.Telemetry;

namespace Foundry.Telemetry.Tests;

public sealed class PostHogRemoteDiagnosticsSinkTests
{
    [Fact]
    public async Task Configure_NewDestinationRetiresOldExporterAndUsesNewRecordContext()
    {
        var original = new RecordingExporter();
        var replacement = new RecordingExporter();
        var configured = new List<RemoteDiagnosticsOptions>();
        await using var service = new PostHogRemoteDiagnosticsSink((options, _) =>
        {
            configured.Add(options);
            return configured.Count == 1 ? original : replacement;
        });
        RemoteDiagnosticsOptions firstOptions = RemoteDiagnosticsTestData.EnabledOptions();
        service.Configure(firstOptions, RemoteDiagnosticsTestData.Context());
        service.Configure(firstOptions with { ProjectToken = "phc_replacement" },
            RemoteDiagnosticsTestData.Context() with { SessionId = "session-2" });
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "new destination", new InvalidOperationException("failed")));
        await service.FlushAsync(TestContext.Current.CancellationToken);
        await service.DisposeAsync();

        Assert.Equal(2, configured.Count);
        Assert.True(original.IsDisposed);
        Assert.Empty(original.Records);
        Assert.Equal("session-2", Assert.Single(replacement.Records).Attributes["session.id"]);
        Assert.Equal("phc_replacement", configured[1].ProjectToken);
    }

    [Fact]
    public async Task Configure_ContextChangePreservesPendingRecordsAndExistingDestination()
    {
        var exporter = new BlockingExporter();
        int factoryCalls = 0;
        await using var service = new PostHogRemoteDiagnosticsSink((_, _) => { factoryCalls++; return exporter; });
        RemoteDiagnosticsOptions options = RemoteDiagnosticsTestData.EnabledOptions();
        service.Configure(options, RemoteDiagnosticsTestData.Context());
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "first", new InvalidOperationException("failed")));
        Assert.True(exporter.Started.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "pending", new InvalidOperationException("failed")));
        service.Configure(options, RemoteDiagnosticsTestData.Context() with { SessionId = "session-2" });
        exporter.Release.Set();
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "updated", new InvalidOperationException("failed")));
        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, factoryCalls);
        Assert.Equal(["first", "pending", "updated"], exporter.Records.Select(record => record.Body).ToArray());
        Assert.Equal("session-1", exporter.Records[1].Attributes["session.id"]);
        Assert.Equal("session-2", exporter.Records[2].Attributes["session.id"]);
    }

    [Fact]
    public async Task Emit_CapturesAllSixLogLevelsAndReservesStrictChannelForErrorExceptions()
    {
        var exporter = new RecordingExporter();
        var logs = new List<RemoteDiagnosticRecord>();
        await using var service = new PostHogRemoteDiagnosticsSink((_, _) => exporter, logCapture: logs.Add);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());

        foreach (LogEventLevel level in Enum.GetValues<LogEventLevel>())
        {
            service.Emit(RemoteDiagnosticsTestData.LogEvent(level, "ordinary log"));
            service.Emit(RemoteDiagnosticsTestData.LogEvent(level, "exception log", new InvalidOperationException("failed")));
        }
        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(12, logs.Count);
        Assert.All(Enum.GetValues<LogEventLevel>(), level => Assert.Equal(2, logs.Count(record => record.Level == level)));
        Assert.Equal([LogEventLevel.Error, LogEventLevel.Fatal], exporter.Records.Select(record => record.Level).ToArray());
        Assert.Equal(2, exporter.ExceptionEvents.Count);
    }

    [Fact]
    public async Task Emit_WhenDisabled_DoesNotCreateExporter()
    {
        int factoryCalls = 0;
        await using var service = new PostHogRemoteDiagnosticsSink(
            (_, _) =>
            {
                factoryCalls++;
                return new RecordingExporter();
            });

        service.Configure(RemoteDiagnosticsTestData.EnabledOptions() with { IsEnabled = false }, RemoteDiagnosticsTestData.Context());
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "failed", new InvalidOperationException("failed")));
        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task Disable_StopsAcceptanceAndConfigureCanReenableExistingTransport()
    {
        var exporter = new RecordingExporter();
        await using var service = CreateService(exporter);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());

        service.Disable();
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "disabled", new InvalidOperationException("failed")));
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "re-enabled", new InvalidOperationException("failed")));
        await service.FlushAsync(TestContext.Current.CancellationToken);

        RemoteDiagnosticRecord record = Assert.Single(exporter.Records);
        Assert.Equal("re-enabled", record.Body);
    }

    [Fact]
    public async Task Disable_DropsBufferedRecordsButAllowsInFlightExportToFinish()
    {
        var exporter = new BlockingExporter();
        await using var service = CreateService(exporter);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "in-flight", new InvalidOperationException("failed")));
        Assert.True(exporter.Started.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "buffered", new InvalidOperationException("failed")));

        service.Disable();
        exporter.Release.Set();
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "after-reenable", new InvalidOperationException("failed")));
        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["in-flight", "after-reenable"], exporter.Records.Select(static record => record.Body).ToArray());
    }

    [Fact]
    public async Task Disable_ReenableResetsRateLimitAndExceptionDedupeState()
    {
        var exporter = new RecordingExporter();
        await using var service = CreateService(exporter);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        var firstException = new InvalidOperationException("failed");

        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "same failure", firstException));
        for (int index = 1; index < 5; index++)
        {
            service.Emit(RemoteDiagnosticsTestData.LogEvent(
                LogEventLevel.Error,
                "same failure",
                new InvalidOperationException("failed")));
        }

        Assert.True(SpinWait.SpinUntil(
            () => exporter.Records.Count == 5,
            TimeSpan.FromSeconds(2)));

        service.Disable();
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "same failure", firstException));
        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(6, exporter.Records.Count);
        Assert.Equal(6, exporter.ExceptionEvents.Count);
    }

    [Fact]
    public async Task Emit_WhenExporterFails_DoesNotThrowAndContinuesDraining()
    {
        var exporter = new RecordingExporter { ThrowOnExport = true };
        await using var service = CreateService(exporter);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());

        Exception? exception = Record.Exception(() =>
        {
            service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "first", new InvalidOperationException("failed")));
            service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "second", new InvalidOperationException("failed")));
        });
        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Null(exception);
        Assert.Equal(2, exporter.ExportAttempts);
    }

    [Fact]
    public async Task Emit_PreservesTerminalFailureAfterLowerLevelException()
    {
        var exporter = new RecordingExporter();
        await using var service = CreateService(exporter);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        var sharedException = new InvalidOperationException("failed");

        service.Emit(RemoteDiagnosticsTestData.LogEvent(
            LogEventLevel.Error, "Artifact download failed", sharedException, ("OperationId", "deployment-1")));
        service.Emit(RemoteDiagnosticsTestData.LogEvent(
            LogEventLevel.Error, "Deployment failed", sharedException,
            ("OperationId", "deployment-1"), ("Outcome", "failed"), ("FailureCode", "download_failed")));
        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, exporter.Records.Count);
        RemoteDiagnosticRecord terminal = exporter.Records[1];
        Assert.Equal("failed", terminal.Attributes["operation.outcome"]);
        Assert.Equal("download_failed", terminal.Attributes["failure.code"]);
        Assert.NotNull(terminal.Exception);
        Assert.Single(exporter.ExceptionEvents);
    }

    [Fact]
    public async Task Emit_SameExceptionInDifferentOperations_IsRetained()
    {
        var exporter = new RecordingExporter();
        await using var service = CreateService(exporter);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        var sharedException = new InvalidOperationException("failed");

        service.Emit(RemoteDiagnosticsTestData.LogEvent(
            LogEventLevel.Error,
            "first failed",
            sharedException,
            ("OperationId", "operation-1")));
        service.Emit(RemoteDiagnosticsTestData.LogEvent(
            LogEventLevel.Error,
            "second failed",
            sharedException,
            ("OperationId", "operation-2")));
        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, exporter.Records.Count);
        Assert.Equal(2, exporter.ExceptionEvents.Count);
    }

    [Fact]
    public async Task Emit_WarningDoesNotSuppressLaterErrorTracking()
    {
        var exporter = new RecordingExporter();
        await using var service = CreateService(exporter);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        var exception = new InvalidOperationException("failed");

        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Warning, "retrying", exception));
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "failed", exception));
        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Single(exporter.Records);
        Assert.Single(exporter.ExceptionEvents);
    }

    [Fact]
    public async Task Emit_RateLimitedExceptionDoesNotSuppressLaterTerminalErrorTracking()
    {
        var exporter = new RecordingExporter();
        await using var service = CreateService(exporter);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        for (int index = 0; index < 5; index++)
        {
            service.Emit(RemoteDiagnosticsTestData.LogEvent(
                LogEventLevel.Error, "download failed", new InvalidOperationException("failed")));
        }

        var exception = new InvalidOperationException("failed");
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "download failed", exception));
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "deployment failed", exception));
        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, service.DroppedRecordCount);
        Assert.Equal(6, exporter.Records.Count);
        Assert.Equal(6, exporter.ExceptionEvents.Count);
    }

    [Fact]
    public async Task Emit_RepeatedWarningsAreAllCapturedWithoutEnteringErrorTracking()
    {
        var exporter = new RecordingExporter();
        var logs = new List<RemoteDiagnosticRecord>();
        await using var service = new PostHogRemoteDiagnosticsSink((_, _) => exporter, logCapture: logs.Add);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());

        for (int index = 0; index < 20; index++)
        {
            service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Warning, "same warning"));
        }

        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(20, logs.Count);
        Assert.Empty(exporter.Records);
    }

    [Theory]
    [InlineData("OperationId")]
    [InlineData("FailedOperationName")]
    [InlineData("ToolName")]
    [InlineData("CurrentOperation")]
    [InlineData("ProcessOperation")]
    public async Task Emit_DistinguishesOperationAndToolContext(string propertyName)
    {
        var exporter = new RecordingExporter();
        await using var service = CreateService(exporter);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        for (int index = 0; index < 8; index++)
        {
            service.Emit(RemoteDiagnosticsTestData.LogEvent(
                LogEventLevel.Error, "same failure", new InvalidOperationException("failed"), (propertyName, $"value-{index}")));
        }

        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(8, exporter.Records.Count);
    }

    [Fact]
    public async Task Emit_PreservesEveryInformationStepIncludingRepetitions()
    {
        var exporter = new RecordingExporter();
        var logs = new List<RemoteDiagnosticRecord>();
        await using var service = new PostHogRemoteDiagnosticsSink((_, _) => exporter, logCapture: logs.Add);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        for (int index = 0; index < 8; index++)
        {
            service.Emit(RemoteDiagnosticsTestData.LogEvent(
                LogEventLevel.Information, "Step starting",
                properties: [("OperationId", "deployment-1"), ("StepName", $"step-{index}")]));
        }

        for (int index = 0; index < 8; index++)
        {
            service.Emit(RemoteDiagnosticsTestData.LogEvent(
                LogEventLevel.Information, "Step starting",
                properties: [("OperationId", "deployment-1"), ("StepName", "step-0")]));
        }

        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(16, logs.Count);
        Assert.Equal(8, logs.Select(record => record.Attributes["StepName"]).Distinct().Count());
        Assert.Equal(9, logs.Count(record => Equals(record.Attributes["StepName"], "step-0")));
        Assert.Empty(exporter.Records);
    }

    [Fact]
    public async Task Emit_ReportsErrorTrackingThrottleLossWithoutDroppingLogs()
    {
        var exporter = new RecordingExporter();
        var logs = new List<RemoteDiagnosticRecord>();
        await using var service = new PostHogRemoteDiagnosticsSink((_, _) => exporter, logCapture: logs.Add);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        for (int index = 0; index < 8; index++)
        {
            service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "failed", new InvalidOperationException("failed")));
        }

        service.Emit(RemoteDiagnosticsTestData.LogEvent(
            LogEventLevel.Error, "another failure", new InvalidOperationException("failed")));
        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(6, exporter.Records.Count);
        Assert.Equal(9, logs.Count);
        Assert.Equal(3L, exporter.Records[^1].Attributes["diagnostics.dropped_record_count"]);
    }

    [Fact]
    public async Task Emit_WhenQueueIsFull_DropsWithoutBlocking()
    {
        var exporter = new BlockingExporter();
        await using var service = CreateService(exporter, queueCapacity: 1);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "first", new InvalidOperationException("failed")));
        Assert.True(exporter.Started.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "second", new InvalidOperationException("failed")));
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "third", new InvalidOperationException("failed")));

        Assert.Equal(1, service.DroppedRecordCount);
        exporter.Release.Set();
        await service.FlushAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FlushAsync_WhenExporterIsBlocked_ObservesCancellation()
    {
        var exporter = new BlockingExporter();
        await using var service = CreateService(exporter);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "failed", new InvalidOperationException("failed")));
        Assert.True(exporter.Started.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.FlushAsync(cancellation.Token));

        exporter.Release.Set();
    }

    [Theory]
    [InlineData("RemoteDiagnosticsInternal")]
    [InlineData("PostHogTransportInternal")]
    public async Task Emit_InternalExporterEvent_IsExcludedFromErrorTracking(string internalProperty)
    {
        var exporter = new RecordingExporter();
        await using var service = CreateService(exporter);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());

        service.Emit(RemoteDiagnosticsTestData.LogEvent(
            LogEventLevel.Error,
            "exporter failure",
            new InvalidOperationException("failed"),
            properties: (internalProperty, true)));
        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Empty(exporter.Records);
    }

    private static PostHogRemoteDiagnosticsSink CreateService(
        IRemoteDiagnosticsExporter exporter,
        int queueCapacity = 32) =>
        new((_, _) => exporter, queueCapacity);

    private class RecordingExporter : IRemoteDiagnosticsExporter
    {
        private readonly RecordingEventClient _eventClient = new();

        public List<string> ExceptionEvents => _eventClient.Events;

        public List<RemoteDiagnosticRecord> Records { get; } = [];

        public int ExportAttempts { get; private set; }

        public bool ThrowOnExport { get; init; }

        public bool IsDisposed { get; private set; }

        public virtual ValueTask ExportAsync(RemoteDiagnosticRecord record, CancellationToken cancellationToken)
        {
            ExportAttempts++;
            if (ThrowOnExport)
            {
                throw new InvalidOperationException("export failed");
            }

            Records.Add(record);
            new PostHogExceptionTracker(_eventClient, "install-1").Track(record);
            return ValueTask.CompletedTask;
        }

        public virtual Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public virtual ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingEventClient : IPostHogEventClient
    {
        public List<string> Events { get; } = [];

        public bool Capture(string distinctId, string eventName, Dictionary<string, object> properties, DateTimeOffset timestamp)
        {
            Events.Add(eventName);
            return true;
        }

        public Task FlushAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingExporter : RecordingExporter
    {
        public ManualResetEventSlim Started { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public override ValueTask ExportAsync(RemoteDiagnosticRecord record, CancellationToken cancellationToken)
        {
            Started.Set();
            Release.Wait(cancellationToken);
            return base.ExportAsync(record, cancellationToken);
        }
    }
}
