// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Events;
using Foundry.Telemetry;

namespace Foundry.Telemetry.Tests;

public sealed class PostHogRemoteDiagnosticsSinkTests
{
    [Fact]
    public async Task Emit_FiltersLevelsAndAllowsExplicitInformationBoundaries()
    {
        var exporter = new RecordingExporter();
        await using var service = CreateService(exporter);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());

        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Debug, "debug"));
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Information, "ordinary info"));
        service.Emit(RemoteDiagnosticsTestData.LogEvent(
            LogEventLevel.Information,
            "workflow boundary",
            properties: ("RemoteDiagnostic", true)));
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Warning, "warning"));
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "error"));
        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [LogEventLevel.Information, LogEventLevel.Warning, LogEventLevel.Error],
            exporter.Records.Select(static record => record.Level).ToArray());
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
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "failed"));
        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task Emit_WhenExporterFails_DoesNotThrowAndContinuesDraining()
    {
        var exporter = new RecordingExporter { ThrowOnExport = true };
        await using var service = CreateService(exporter);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());

        Exception? exception = Record.Exception(() =>
        {
            service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "first"));
            service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "second"));
        });
        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Null(exception);
        Assert.Equal(2, exporter.ExportAttempts);
    }

    [Fact]
    public async Task Emit_DropsDuplicateExceptionInstance()
    {
        var exporter = new RecordingExporter();
        await using var service = CreateService(exporter);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        var sharedException = new InvalidOperationException("failed");

        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "failed", sharedException));
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "failed again", sharedException));
        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Single(exporter.Records);
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
    }

    [Fact]
    public async Task Emit_RateLimitsRepeatedFingerprint()
    {
        var exporter = new RecordingExporter();
        await using var service = CreateService(exporter);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());

        for (int index = 0; index < 8; index++)
        {
            service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Warning, "same warning"));
        }

        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(5, exporter.Records.Count);
    }

    [Fact]
    public async Task Emit_WhenQueueIsFull_DropsWithoutBlocking()
    {
        var exporter = new BlockingExporter();
        await using var service = CreateService(exporter, queueCapacity: 1);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "first"));
        Assert.True(exporter.Started.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "second"));
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "third"));

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
        service.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "failed"));
        Assert.True(exporter.Started.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.FlushAsync(cancellation.Token));

        exporter.Release.Set();
    }

    [Fact]
    public async Task Emit_InternalExporterEvent_IsExcluded()
    {
        var exporter = new RecordingExporter();
        await using var service = CreateService(exporter);
        service.Configure(RemoteDiagnosticsTestData.EnabledOptions(), RemoteDiagnosticsTestData.Context());

        service.Emit(RemoteDiagnosticsTestData.LogEvent(
            LogEventLevel.Error,
            "exporter failure",
            properties: ("RemoteDiagnosticsInternal", true)));
        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Empty(exporter.Records);
    }

    [Fact]
    public void CreateLogEvent_ReconstructsOnlySanitizedRecordData()
    {
        var record = new RemoteDiagnosticRecord(
            DateTimeOffset.UtcNow,
            LogEventLevel.Error,
            "Deployment failed for {Path}",
            new Dictionary<string, object>
            {
                ["service.name"] = "foundry.deploy",
                ["operation.id"] = "operation-1"
            },
            new RemoteDiagnosticException("System.InvalidOperationException", "redacted", "at Foundry.Run()", []));

        LogEvent logEvent = PostHogDiagnosticsExporter.CreateLogEvent(record);

        Assert.Null(logEvent.Exception);
        Assert.Equal("Deployment failed for {Path}", logEvent.RenderMessage());
        Assert.Equal("foundry.deploy", Assert.IsType<ScalarValue>(logEvent.Properties["service.name"]).Value);
        Assert.Equal("redacted", Assert.IsType<ScalarValue>(logEvent.Properties["exception.message"]).Value);
    }

    private static PostHogRemoteDiagnosticsSink CreateService(
        IRemoteDiagnosticsExporter exporter,
        int queueCapacity = 32) =>
        new((_, _) => exporter, queueCapacity);

    private class RecordingExporter : IRemoteDiagnosticsExporter
    {
        public List<RemoteDiagnosticRecord> Records { get; } = [];

        public int ExportAttempts { get; private set; }

        public bool ThrowOnExport { get; init; }

        public virtual ValueTask ExportAsync(RemoteDiagnosticRecord record, CancellationToken cancellationToken)
        {
            ExportAttempts++;
            if (ThrowOnExport)
            {
                throw new InvalidOperationException("export failed");
            }

            Records.Add(record);
            return ValueTask.CompletedTask;
        }

        public virtual Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
