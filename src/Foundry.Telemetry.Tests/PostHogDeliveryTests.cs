// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Net;
using System.IO.Compression;
using System.Text.Json;
using Foundry.Telemetry;
using Foundry.Utilities.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PostHog;
using Serilog.Events;
using Serilog.Core;

namespace Foundry.Telemetry.Tests;

[Collection(RemoteDiagnosticsSinkCollection.Name)]
public sealed class PostHogDeliveryTests
{
    [Fact]
    public async Task DisposeDeadline_DoesNotWaitIndefinitelyForRetiredExporter()
    {
        DelayedDisposalExporter? retired = null;
        await using var sink = new PostHogRemoteDiagnosticsSink((_, _, report) =>
        {
            if (retired is null) return retired = new DelayedDisposalExporter(report);
            return new EmptyExporter();
        });
        sink.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        sink.Disable();
        sink.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        await retired!.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        try
        {
            await sink.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(retired.Finished.Task.IsCompleted);
        }
        finally
        {
            retired.Release.TrySetResult();
            await retired.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Shutdown_DisposalWarningReachesRealLogPipelineBeforeItCloses()
    {
        var transport = new RecordingLogTransport();
        DisposalExporter? exporter = null;
        await using var sink = new PostHogRemoteDiagnosticsSink((_, _, report) => exporter = new DisposalExporter(report),
            logPipelineFactory: (_, _) => new ReliableLogPipeline(transport, null, startDelivery: false));
        sink.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());

        await Task.WhenAll(sink.FlushAsync(TestContext.Current.CancellationToken), sink.FlushAsync(TestContext.Current.CancellationToken));
        await sink.DisposeAsync();

        Assert.Equal("batch_exception", Assert.Single(transport.Records).Attributes["FailureReason"]);
        Assert.Equal(1, exporter!.DisposeCount);
    }

    [Fact]
    public async Task ShutdownDeadline_StopsWaitingForSdkDisposalAndKeepsLateWarningsLocal()
    {
        var transport = new RecordingLogTransport();
        DelayedDisposalExporter? exporter = null;
        await using var sink = new PostHogRemoteDiagnosticsSink((_, _, report) => exporter = new DelayedDisposalExporter(report),
            logPipelineFactory: (_, _) => new ReliableLogPipeline(transport, null, startDelivery: false));
        sink.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        using var deadline = new CancellationTokenSource();
        Task shutdown = sink.FlushAsync(deadline.Token);
        await exporter!.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await deadline.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => shutdown);
        exporter.Release.SetResult();
        await exporter.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await sink.DisposeAsync();

        Assert.Empty(transport.Records);
        Assert.Equal(1, exporter.DisposeCount);
    }

    [Fact]
    public async Task DeliveryWarning_PreservesIdentityAcrossLocalNormalizationAndRemoteLogs()
    {
        var local = new RecordingLogSink();
        var remote = new List<RemoteDiagnosticRecord>();
        Action<ExceptionDeliveryFailure>? report = null;
        await using var sink = new PostHogRemoteDiagnosticsSink((_, _, callback) =>
        {
            report = callback;
            return new EmptyExporter();
        }, logCapture: remote.Add);
        sink.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        Serilog.ILogger previous = Serilog.Log.Logger;
        Serilog.ILogger logger = FoundryLogConfiguration.CreateDebugLogger("foundry.deploy", "session-1", LogEventLevel.Verbose, local);
        using var loggerLifetime = (IDisposable)logger;
        try
        {
            Serilog.Log.Logger = logger;
            Assert.NotNull(report);
            report(new("capture_rejected"));

            LogEvent localEvent = Assert.Single(local.Events);
            RemoteDiagnosticRecord remoteRecord = Assert.Single(remote);
            foreach (string property in new[] { "diagnostics.record_id", "diagnostics.process_id", "diagnostics.sequence" })
                Assert.Equal(Assert.IsType<ScalarValue>(localEvent.Properties[property]).Value, remoteRecord.Attributes[property]);
            Assert.Equal(localEvent.Timestamp, remoteRecord.Timestamp);
        }
        finally
        {
            Serilog.Log.Logger = previous;
        }
    }

    [Fact]
    public async Task Flush_SendsSanitizedExceptionThroughRealSdk()
    {
        using var transport = new RecordingTransport(HttpStatusCode.OK);
        var logs = new ConcurrentQueue<RemoteDiagnosticRecord>();
        await using var sink = CreateSink(transport, logs);
        sink.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        sink.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "boot media failed",
            new InvalidOperationException("Failed for C:\\Users\\Private\\image.wim")));

        await sink.FlushAsync(TestContext.Current.CancellationToken);

        using JsonDocument document = JsonDocument.Parse(Assert.Single(transport.Bodies));
        JsonElement captured = Assert.Single(document.RootElement.GetProperty("batch").EnumerateArray());
        Assert.Equal("$exception", captured.GetProperty("event").GetString());
        JsonElement properties = captured.GetProperty("properties");
        Assert.Equal("session-1", properties.GetProperty("$session_id").GetString());
        Assert.Equal("System.InvalidOperationException", properties.GetProperty("$exception_type").GetString());
        Assert.Single(properties.GetProperty("$exception_list").EnumerateArray());
        Assert.False(properties.GetProperty("$process_person_profile").GetBoolean());
        Assert.DoesNotContain("Private", captured.GetRawText(), StringComparison.Ordinal);
        Assert.Single(logs);
    }

    [Fact]
    public async Task Flush_HttpRejectionProducesOneSafeLogWithoutAnotherExceptionRequest()
    {
        using var transport = new RecordingTransport(HttpStatusCode.BadRequest);
        var logs = new ConcurrentQueue<RemoteDiagnosticRecord>();
        await using var sink = CreateSink(transport, logs);
        sink.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        sink.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "operation failed", new InvalidOperationException("failed")));

        await sink.FlushAsync(TestContext.Current.CancellationToken);

        RemoteDiagnosticRecord failure = Assert.Single(logs, record => record.Attributes.ContainsKey("FailureReason"));
        Assert.Equal("http_failure", failure.Attributes["FailureReason"]);
        Assert.Equal(400, failure.Attributes["HttpStatusCode"]);
        Assert.False(failure.ShouldTrackException);
        Assert.Null(failure.Exception);
        Assert.DoesNotContain("secret-response", JsonSerializer.Serialize(failure), StringComparison.Ordinal);
        Assert.Single(transport.Bodies);
    }

    [Fact]
    public async Task BackgroundBatch_HttpRejectionIsObservedBeforeExplicitFlush()
    {
        using var transport = new RecordingTransport(HttpStatusCode.BadRequest);
        var observed = new TaskCompletionSource<ExceptionDeliveryFailure>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var exporter = new PostHogDiagnosticsExporter(RemoteDiagnosticsTestData.EnabledOptions(),
            failure => observed.TrySetResult(failure), transport);
        RemoteDiagnosticRecord record = RemoteDiagnosticPropertyPolicy.CreateSanitizedRecord(
            RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "boot media failed", new InvalidOperationException("failed")),
            RemoteDiagnosticsTestData.Context());

        for (int index = 0; index < 20; index++)
            await exporter.ExportAsync(record, TestContext.Current.CancellationToken);
        ExceptionDeliveryFailure failure = await observed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal("http_failure", failure.Reason);
        Assert.Equal(400, failure.HttpStatusCode);
        Assert.Single(transport.Bodies);
    }

    [Fact]
    public async Task SdkQueueOverflow_ReportsOldestLossEvenWhenCaptureReturnsTrue()
    {
        using var transport = new RecordingTransport(HttpStatusCode.OK);
        var failures = new List<ExceptionDeliveryFailure>();
        using var loggerFactory = new PostHogDeliveryLoggerFactory(failures.Add);
        await using var client = new PostHogClient(Options.Create(new PostHogOptions
        {
            ProjectToken = "phc_test",
            HostUrl = new Uri("https://example.invalid"),
            MaxQueueSize = 1,
            FlushAt = 20,
            FlushInterval = TimeSpan.FromHours(1)
        }), httpClientFactory: transport, loggerFactory: loggerFactory);

        Assert.True(client.Capture("install-1", "first"));
        Assert.True(client.Capture("install-1", "second"));
        await client.FlushAsync();

        Assert.Equal("sdk_queue_drop_oldest", Assert.Single(failures).Reason);
        using JsonDocument document = JsonDocument.Parse(Assert.Single(transport.Bodies));
        Assert.Equal("second", Assert.Single(document.RootElement.GetProperty("batch").EnumerateArray()).GetProperty("event").GetString());
    }

    [Fact]
    public void CaptureRejected_ReportsRejectionWithoutClaimingQueueOverflow()
    {
        var failures = new List<ExceptionDeliveryFailure>();
        var tracker = new PostHogExceptionTracker(new RejectingClient(), "install-1", failures.Add);
        tracker.Track(new RemoteDiagnosticRecord(DateTimeOffset.UtcNow, LogEventLevel.Error, "failed", new Dictionary<string, object>(),
            new RemoteDiagnosticException("System.InvalidOperationException", "failed", null, [])));

        Assert.Equal("capture_rejected", Assert.Single(failures).Reason);
    }

    [Fact]
    public void SdkLogger_ReportingFailureDoesNotEscapeIntoSdk()
    {
        using var factory = new PostHogDeliveryLoggerFactory(_ => throw new IOException("logging failed"));
        factory.CreateLogger("PostHog.Library.AsyncBatchHandler").Log(LogLevel.Error, new EventId(500), "state",
            new InvalidOperationException("failed"), static (_, _) => "unused");
    }

    [Fact]
    public async Task ConsentReenabled_ReportsNewFailuresButIgnoresRetiredCallbacks()
    {
        var callbacks = new List<Action<ExceptionDeliveryFailure>>();
        var logs = new List<RemoteDiagnosticRecord>();
        await using var sink = new PostHogRemoteDiagnosticsSink((_, _, report) =>
        {
            callbacks.Add(report);
            return new EmptyExporter();
        }, logCapture: logs.Add);
        sink.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        sink.Disable();
        callbacks[0](new("batch_exception"));
        Assert.Empty(logs);
        sink.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        Assert.Equal(2, callbacks.Count);
        callbacks[0](new("batch_exception"));
        callbacks[1](new("capture_rejected"));

        Assert.Equal("capture_rejected", Assert.Single(logs).Attributes["FailureReason"]);
    }

    [Theory]
    [InlineData("PostHog.Library.AsyncBatchHandler", 111, "sdk_queue_drop_oldest")]
    [InlineData("PostHog.Library.AsyncBatchHandler", 500, "batch_exception")]
    [InlineData("PostHog.Library.AsyncBatchHandler", 108, null)]
    [InlineData("Other.Category", 500, null)]
    public void SdkLogger_OnlyReportsKnownDeliveryEventsAndNeverFormatsSdkData(string category, int eventId, string? expectedReason)
    {
        var failures = new List<ExceptionDeliveryFailure>();
        using var factory = new PostHogDeliveryLoggerFactory(failures.Add);
        factory.CreateLogger(category).Log(LogLevel.Error, new EventId(eventId), "secret-state",
            new InvalidOperationException("secret-exception"), static (_, _) => throw new InvalidOperationException("Must not format SDK data"));

        if (expectedReason is null) Assert.Empty(failures);
        else Assert.Equal(expectedReason, Assert.Single(failures).Reason);
    }

    private static PostHogRemoteDiagnosticsSink CreateSink(RecordingTransport transport, ConcurrentQueue<RemoteDiagnosticRecord> logs) =>
        new((options, _, report) => new PostHogDiagnosticsExporter(options, report, transport), logCapture: logs.Enqueue);

    private sealed class RecordingTransport(HttpStatusCode status) : HttpMessageHandler, IHttpClientFactory
    {
        public ConcurrentQueue<string> Bodies { get; } = new();
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/batch", request.RequestUri!.AbsolutePath);
            await using Stream body = await request.Content!.ReadAsStreamAsync(cancellationToken);
            await using Stream decoded = request.Content.Headers.ContentEncoding.Contains("gzip")
                ? new GZipStream(body, CompressionMode.Decompress, leaveOpen: true) : body;
            using var reader = new StreamReader(decoded);
            Bodies.Enqueue(await reader.ReadToEndAsync(cancellationToken));
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(status == HttpStatusCode.OK ? "{\"status\":1}" :
                    "{\"type\":\"validation_error\",\"detail\":\"secret-response\",\"code\":\"invalid\"}")
            };
        }
    }

    private sealed class EmptyExporter : IRemoteDiagnosticsExporter
    {
        public ValueTask ExportAsync(RemoteDiagnosticRecord record, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingLogSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class RejectingClient : IPostHogEventClient
    {
        public bool Capture(string distinctId, string eventName, Dictionary<string, object> properties, DateTimeOffset timestamp) => false;
        public Task FlushAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DisposalExporter(Action<ExceptionDeliveryFailure> report) : IRemoteDiagnosticsExporter
    {
        public int DisposeCount { get; private set; }
        public ValueTask ExportAsync(RemoteDiagnosticRecord record, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            report(new("batch_exception"));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingLogTransport : ILogBatchTransport
    {
        public ConcurrentQueue<RemoteDiagnosticRecord> Records { get; } = new();
        public Task<LogBatchResult> SendAsync(IReadOnlyList<RemoteDiagnosticRecord> records, CancellationToken cancellationToken)
        {
            foreach (RemoteDiagnosticRecord record in records) Records.Enqueue(record);
            return Task.FromResult(new LogBatchResult(LogBatchDisposition.Accepted));
        }
        public void Dispose() { }
    }

    private sealed class DelayedDisposalExporter(Action<ExceptionDeliveryFailure> report) : IRemoteDiagnosticsExporter
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCount { get; private set; }
        public ValueTask ExportAsync(RemoteDiagnosticRecord record, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            Started.SetResult();
            await Release.Task;
            report(new("batch_exception"));
            Finished.SetResult();
        }
    }
}
