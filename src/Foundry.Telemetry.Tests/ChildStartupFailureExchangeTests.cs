// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Text.Json;
using Foundry.Telemetry;
using Serilog.Events;

namespace Foundry.Telemetry.Tests;

public sealed class ChildStartupFailureExchangeTests
{
    private static readonly RemoteDiagnosticsOptions Options = new(true, "https://example.test", "public", "install");
    private static readonly TelemetryContext ParentContext = new(TelemetryApps.FoundryBootstrap, "2.0", "release", "winpe",
        "release", "usb", "x64", "en-US", "session-1");
    private static readonly RemoteDiagnosticsContext ChildContext = new(TelemetryApps.FoundryConnect, "1.2", "release",
        "winpe", "x64", "en-US", "session-1", "foundry_connect@1.2");

    [Fact]
    public void Capture_PersistsOriginalSanitizedExceptionAndStableIdentityWithoutTransport()
    {
        using var folder = new TestFolder();
        Exception failure = CreateException();
        Guid? id = Capture(folder, failure);
        Assert.NotNull(id);
        ChildStartupFailureRecord stored = ChildStartupFailureExchange.Read(folder.Directory, folder.Launch, ChildContext.App)!;
        Assert.Equal(id, stored.RecordId);
        Assert.Equal(typeof(IOException).FullName, stored.Diagnostic.Exception!.Type);
        Assert.Contains(nameof(CreateException), stored.Diagnostic.Exception.StackTrace);
        Assert.Equal(typeof(InvalidOperationException).FullName, Assert.Single(stored.Diagnostic.Exception.InnerExceptions).Type);
        Assert.Equal(ChildContext.App, stored.Diagnostic.Attributes["service.name"]);
        Assert.DoesNotContain("private-secret", File.ReadAllText(folder.Exchange), StringComparison.Ordinal);
        Assert.Equal(id, Capture(folder, new IOException("Later observation")));
    }

    [Fact]
    public void Capture_RequiresResolvedConsentAndPreservesFailureLargerThanLegacyLimit()
    {
        using var folder = new TestFolder();
        Assert.Null(ChildStartupFailureExchange.TryCapture(folder.Directory, folder.Launch, Options with { IsEnabled = false },
            ChildContext, Failure(new IOException("Failed"))));
        Assert.False(File.Exists(folder.Exchange));
        var largeFailure = new AggregateException(Enumerable.Range(0, 200).Select(_ => new IOException(new string('x', 2048))));
        Assert.NotNull(Capture(folder, largeFailure));
        Assert.True(new FileInfo(folder.Exchange).Length > 256 * 1024);
        Assert.True(new FileInfo(folder.Exchange).Length <= ChildStartupFailureExchange.MaximumBytes);
    }

    [Fact]
    public async Task Import_PreservesFullChildLogSeparatelyFromConservativeErrorTracking()
    {
        using var folder = new TestFolder();
        string message = "Failed to load C:\\Drivers\\network.inf for {Tenant}";
        LogEvent source = RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, message, CreateException(),
            ("Tenant", "ExampleTenant"), ("password", "private-secret"), ("UnrestrictedDetail", new string('x', 5000)));
        Guid? id = ChildStartupFailureExchange.TryCapture(folder.Directory, folder.Launch, Options, ChildContext, source);
        Assert.NotNull(id);
        ChildStartupFailureRecord stored = ChildStartupFailureExchange.Read(folder.Directory, folder.Launch, ChildContext.App)!;
        Assert.NotNull(stored.Log);
        Assert.Contains("C:\\Drivers\\network.inf", stored.Log.Body, StringComparison.Ordinal);
        Assert.Contains("ExampleTenant", stored.Log.Body, StringComparison.Ordinal);
        Assert.Equal(5000, stored.Log.Attributes["UnrestrictedDetail"].ToString()!.Length);
        Assert.DoesNotContain("private-secret", JsonSerializer.Serialize(stored), StringComparison.Ordinal);
        Assert.False(stored.Diagnostic.Attributes.ContainsKey("UnrestrictedDetail"));
        var transport = new RecordingTransport { AcknowledgeLogs = true };
        await using var pipeline = Create(transport, folder.Journal);
        Assert.True(pipeline.ImportChildStartupFailure(folder.Directory, folder.Launch, ChildContext.App, true, id));
        await pipeline.ShutdownAsync(TestContext.Current.CancellationToken);
        BootstrapPendingRecord sent = Assert.Single(transport.Records, record => record.Destination == BootstrapTelemetryDestination.Log);
        Assert.Equal(stored.LogRecordId, sent.Id);
        Assert.Equal(stored.Log.Body, sent.Diagnostic!.Body);
    }

    [Theory]
    [InlineData("relative-launch")]
    [InlineData(@"C:relative-launch")]
    [InlineData(@"\\unreachable-host\share\launch")]
    [InlineData(@"\\?\C:\launch")]
    public void Exchange_RejectsNonLocalOrRelativePathsBeforeFileAccess(string directory)
    {
        Guid launch = Guid.NewGuid();
        Assert.Null(ChildStartupFailureExchange.TryCapture(directory, launch, Options, ChildContext, Failure(CreateException())));
        Assert.Null(ChildStartupFailureExchange.Read(directory, launch, ChildContext.App));
        ChildStartupFailureExchange.Retire(directory);
    }

    [Fact]
    public async Task Import_RequiresExitAndPreservesChildContextAndDestinationIds()
    {
        using var folder = new TestFolder();
        Guid id = Capture(folder, CreateException())!.Value;
        var transport = new RecordingTransport();
        await using var pipeline = Create(transport, folder.Journal);
        Assert.False(pipeline.ImportChildStartupFailure(folder.Directory, folder.Launch, ChildContext.App, childExited: false));
        Assert.Empty(pipeline.PendingRecords);
        Assert.True(File.Exists(folder.Exchange));
        Assert.True(pipeline.ImportChildStartupFailure(folder.Directory, folder.Launch, ChildContext.App, childExited: true));
        Assert.False(File.Exists(folder.Exchange));
        await pipeline.ShutdownAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, transport.Records.Count);
        BootstrapPendingRecord exception = Assert.Single(transport.Records, record => record.Destination == BootstrapTelemetryDestination.Exception);
        Assert.Equal(id, exception.Id);
        Assert.Equal(ChildContext.App, exception.Diagnostic!.Attributes["service.name"]);
        Assert.Equal(ChildContext.SessionId, exception.Diagnostic.Attributes["session.id"]);
        Assert.Equal(ChildContext.AppVersion, exception.Diagnostic.Attributes["service.version"]);
        Assert.Equal(ChildContext.Runtime, exception.Diagnostic.Attributes["runtime.name"]);
        Assert.Equal(ChildContext.RuntimeArchitecture, exception.Diagnostic.Attributes["runtime.architecture"]);
        Assert.Equal(ChildContext.Release, exception.Diagnostic.Attributes["service.release"]);
    }

    [Fact]
    public async Task Reimport_PreservesExceptionAttemptLimitAndStableLogIdentityAcrossBoots()
    {
        using var folder = new TestFolder();
        Guid id = Capture(folder, CreateException())!.Value;
        string exchange = File.ReadAllText(folder.Exchange);
        for (int boot = 1; boot <= 4; boot++)
        {
            File.WriteAllText(folder.Exchange, exchange);
            var transport = new RecordingTransport { AcknowledgeLogs = true };
            await using var pipeline = Create(transport, folder.Journal);
            Assert.True(pipeline.ImportChildStartupFailure(folder.Directory, folder.Launch, ChildContext.App, true));
            await pipeline.ShutdownAsync(TestContext.Current.CancellationToken);
            Assert.Equal(boot <= 3 ? 2 : 1, transport.Records.Count);
            BootstrapPendingRecord exception = Assert.Single(pipeline.PendingRecords, record => record.Destination == BootstrapTelemetryDestination.Exception);
            Assert.Equal(id, exception.Id);
            Assert.Equal(Math.Min(boot, 3), exception.Attempts);
            Assert.DoesNotContain(pipeline.PendingRecords, record => record.Destination == BootstrapTelemetryDestination.Log);
            Assert.Equal(JsonSerializer.Deserialize<ChildStartupFailureRecord>(exchange)!.LogRecordId,
                Assert.Single(transport.Records, record => record.Destination == BootstrapTelemetryDestination.Log).Id);
        }
    }

    [Theory]
    [InlineData(false, "install")]
    [InlineData(true, "another-install")]
    public async Task Import_PurgesOptedOutOrWrongInstallationRecords(bool diagnostics, string install)
    {
        using var folder = new TestFolder();
        Capture(folder, CreateException());
        var transport = new RecordingTransport();
        await using var pipeline = Create(transport, folder.Journal, Options with { IsEnabled = diagnostics, InstallId = install });
        Assert.False(pipeline.ImportChildStartupFailure(folder.Directory, folder.Launch, ChildContext.App, true));
        Assert.False(File.Exists(folder.Exchange));
        await pipeline.ShutdownAsync(TestContext.Current.CancellationToken);
        Assert.Empty(transport.Records);
    }

    [Fact]
    public async Task Import_RejectsWrongLaunchWrongApplicationAndOversizedFiles()
    {
        using var folder = new TestFolder();
        Capture(folder, CreateException());
        await using var pipeline = Create(new RecordingTransport(), folder.Journal);
        Assert.False(pipeline.ImportChildStartupFailure(folder.Directory, Guid.NewGuid(), ChildContext.App, true));
        Assert.False(pipeline.ImportChildStartupFailure(folder.Directory, folder.Launch, ChildContext.App, true, Guid.NewGuid()));
        Assert.False(pipeline.ImportChildStartupFailure(folder.Directory, folder.Launch, TelemetryApps.FoundryDeploy, true));
        File.WriteAllText(folder.Exchange, new string('x', ChildStartupFailureExchange.MaximumBytes + 1));
        Assert.False(pipeline.ImportChildStartupFailure(folder.Directory, folder.Launch, ChildContext.App, true));
        Assert.Empty(pipeline.PendingRecords);
    }

    [Fact]
    public async Task Import_ResanitizesUntrustedPropertiesBeforeQueueing()
    {
        using var folder = new TestFolder();
        Capture(folder, CreateException());
        ChildStartupFailureRecord record = JsonSerializer.Deserialize<ChildStartupFailureRecord>(File.ReadAllText(folder.Exchange))!;
        var properties = new Dictionary<string, object>(record.Diagnostic.Attributes)
        {
            ["password"] = "private-secret",
            ["failure.reason"] = "password=private-secret"
        };
        record = record with { Diagnostic = record.Diagnostic with { Attributes = properties } };
        File.WriteAllText(folder.Exchange, JsonSerializer.Serialize(record));
        await using var pipeline = Create(new RecordingTransport(), folder.Journal);
        Assert.True(pipeline.ImportChildStartupFailure(folder.Directory, folder.Launch, ChildContext.App, true));
        Assert.DoesNotContain("private-secret", File.ReadAllText(folder.Journal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportWithoutDurableWrite_LeavesExchangeAvailableForRecovery()
    {
        using var folder = new TestFolder();
        Capture(folder, CreateException());
        Directory.CreateDirectory(folder.Journal);
        await using var pipeline = Create(new RecordingTransport(), folder.Journal);
        Assert.False(pipeline.ImportChildStartupFailure(folder.Directory, folder.Launch, ChildContext.App, true));
        Assert.True(File.Exists(folder.Exchange));
    }

    [Fact]
    public async Task Import_UsesRemainingReplayBudgetAfterIdleTime()
    {
        using var folder = new TestFolder();
        Capture(folder, CreateException());
        var clock = new TestClock();
        var transport = new RecordingTransport();
        await using var pipeline = Create(transport, folder.Journal, time: clock);
        pipeline.StartDelivery(true);
        clock.Timestamp += 600 * clock.TimestampFrequency;
        Assert.True(pipeline.ImportChildStartupFailure(folder.Directory, folder.Launch, ChildContext.App, true));
        await pipeline.ShutdownAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, transport.Records.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recovery_ExpiresOldExceptionsButPreservesLogTimestamp(bool startBeforeImport)
    {
        using var folder = new TestFolder();
        var oldContext = ChildContext with { SessionId = "earlier-boot" };
        LogEvent failure = Failure(CreateException());
        failure = new LogEvent(DateTimeOffset.UtcNow.AddDays(-8), failure.Level, failure.Exception, failure.MessageTemplate,
            failure.Properties.Select(pair => new LogEventProperty(pair.Key, pair.Value)));
        Assert.NotNull(ChildStartupFailureExchange.TryCapture(folder.Directory, folder.Launch, Options, oldContext, failure));
        var transport = new RecordingTransport();
        await using var pipeline = Create(transport, folder.Journal);
        if (startBeforeImport) pipeline.StartDelivery(true);
        Assert.True(pipeline.ImportChildStartupFailure(folder.Directory, folder.Launch, ChildContext.App, true));
        Assert.Equal(startBeforeImport ? 1 : 2, pipeline.PendingRecords.Count);
        pipeline.StartDelivery(true);
        await pipeline.ShutdownAsync(TestContext.Current.CancellationToken);
        BootstrapPendingRecord replayed = Assert.Single(transport.Records);
        Assert.Equal(BootstrapTelemetryDestination.Log, replayed.Destination);
        Assert.Equal(failure.Timestamp, replayed.Timestamp);
    }

    [Fact]
    public async Task ConcurrentCapture_PreservesOneReturnedRecordId()
    {
        using var folder = new TestFolder();
        Guid?[] ids = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(
            () => Capture(folder, new IOException("Failed")), TestContext.Current.CancellationToken)));
        Assert.NotNull(Assert.Single(ids.Distinct()));
    }

    [Fact]
    public async Task ProcessObservation_DoesNotInventAnExceptionAndRetainsReportedId()
    {
        using var folder = new TestFolder();
        Guid? id = ChildStartupFailureExchange.TryCapture(folder.Directory, folder.Launch, Options, ChildContext,
            RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "Startup operation failed"));
        var transport = new RecordingTransport();
        await using var pipeline = Create(transport, folder.Journal);
        Assert.True(pipeline.ImportChildStartupFailure(folder.Directory, folder.Launch, ChildContext.App, true, id));
        await pipeline.ShutdownAsync(TestContext.Current.CancellationToken);
        BootstrapPendingRecord record = Assert.Single(transport.Records);
        Assert.Equal(id, record.Id);
        Assert.Null(record.Diagnostic!.Exception);
    }

    [Fact]
    public async Task ImportedLogs_DoNotConsumeExceptionReplayBudget()
    {
        using var folder = new TestFolder();
        var transport = new RecordingTransport { AcknowledgeLogs = true };
        await using var pipeline = Create(transport, folder.Journal, time: new TestClock());
        for (int index = 0; index < 60; index++)
        {
            Guid launch = Guid.NewGuid();
            string directory = Path.Combine(folder.Directory, launch.ToString("N"));
            Guid? id = ChildStartupFailureExchange.TryCapture(directory, launch, Options, ChildContext, Failure(CreateException()));
            Assert.True(pipeline.ImportChildStartupFailure(directory, launch, ChildContext.App, true, id));
        }
        pipeline.StartDelivery(clockUsable: false);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(30));
        while (transport.Records.Count < 120) await Task.Delay(10, cancellation.Token);
        await pipeline.ShutdownAsync(TestContext.Current.CancellationToken);
        Assert.Equal(120, transport.Records.Count);
    }

    private static Guid? Capture(TestFolder folder, Exception exception) => ChildStartupFailureExchange.TryCapture(
        folder.Directory, folder.Launch, Options, ChildContext, Failure(exception));

    private static LogEvent Failure(Exception exception) => RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error,
        "Startup failed", exception, ("Stage", "configuration"));

    private static Exception CreateException()
    {
        try { throw new IOException("password=private-secret", new InvalidOperationException("inner secret")); }
        catch (IOException exception) { return exception; }
    }

    private static BootstrapTelemetryPipeline Create(RecordingTransport transport, string? path = null,
        RemoteDiagnosticsOptions? options = null, TimeProvider? time = null) => new(
            new TelemetryOptions(false, Options.HostUrl, Options.ProjectToken, Options.InstallId), ParentContext,
            options ?? Options, TelemetryContextFactory.CreateRemoteDiagnosticsContext(ParentContext), path,
            () => transport, time ?? TimeProvider.System, transport);

    private sealed class RecordingTransport : IBootstrapTelemetryTransport, ILogBatchTransport
    {
        internal ConcurrentQueue<BootstrapPendingRecord> Records { get; } = new();
        internal bool AcknowledgeLogs { get; init; }
        public Task<bool> DeliverAsync(BootstrapPendingRecord record, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Records.Enqueue(record);
            return Task.FromResult(AcknowledgeLogs && record.Destination == BootstrapTelemetryDestination.Log);
        }
        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
        public async Task<LogBatchResult> SendAsync(IReadOnlyList<RemoteDiagnosticRecord> records, CancellationToken cancellationToken)
        {
            foreach (RemoteDiagnosticRecord record in records)
                await DeliverAsync(new BootstrapPendingRecord(Guid.Parse(record.Attributes["diagnostics.record_id"].ToString()!),
                    string.Empty, BootstrapTelemetryDestination.Log, record.Timestamp, null, record), cancellationToken);
            return new LogBatchResult(AcknowledgeLogs ? LogBatchDisposition.Accepted : LogBatchDisposition.Retry);
        }
    }

    private sealed class TestClock : TimeProvider
    {
        internal long Timestamp { get; set; }
        public override long GetTimestamp() => Timestamp;
    }

    private sealed class TestFolder : IDisposable
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        internal Guid Launch { get; } = Guid.NewGuid();
        internal TestFolder() => System.IO.Directory.CreateDirectory(Directory);
        internal string Exchange => Path.Combine(Directory, ChildStartupFailureExchange.FileName);
        internal string Journal => Path.Combine(Directory, "pending.jsonl");
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
