// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Services.Runtime;
using Foundry.Core.Models.Runtime;
using Foundry.Telemetry;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Foundry.Connect.Tests;

[Collection(ConnectRemoteDiagnosticsCollection.Name)]
public sealed class RuntimeStartupDiagnosticsTests
{
    [Fact]
    public void FailureNormalizationErrorStillPublishesFailedStatus()
    {
        string? stage = null;
        string? recordId = "unset";
        var startup = new RuntimeStartupDiagnostics(
            (value, _, id) => { stage = value; recordId = id; },
            (_, _, _) => throw new InvalidOperationException("Capture must not run after normalization fails"), Logger.None);

        startup.ReportFailure(new UnrenderableException(), "configuration");

        Assert.Equal(StartupStage.StartupFailed, stage);
        Assert.Null(recordId);
    }

    private sealed class UnrenderableException : Exception
    {
        public override string ToString() => throw new InvalidOperationException("Exception formatting failed");
    }

    [Fact]
    public void FailureCaptureErrorStillPublishesFailedStatusWithoutRecordId()
    {
        string? stage = null;
        string? recordId = "unset";
        var startup = new RuntimeStartupDiagnostics(
            (value, _, id) => { stage = value; recordId = id; },
            (_, _, _) => throw new IOException("unavailable storage"), Logger.None);

        startup.ReportFailure(new InvalidOperationException(), "configuration");

        Assert.Equal(StartupStage.StartupFailed, stage);
        Assert.Null(recordId);
    }

    [Fact]
    public void FailureRecordIsPersistedBeforeStatusAndOriginalExceptionIsRetained()
    {
        var calls = new List<string>();
        var exception = new InvalidOperationException("original failure");
        Guid recordId = Guid.NewGuid();
        Exception? captured = null;
        var startup = new RuntimeStartupDiagnostics(
            (stage, category, id) => calls.Add(stage + ":" + id),
            (error, _, category) => { captured = error; calls.Add("persist"); return recordId; },
            Logger.None);

        startup.ReportFailure(exception, "configuration");
        startup.ReportFailure(exception, "configuration");
        startup.ReportUiReady();

        Assert.Same(exception, captured);
        Assert.Equal(["persist", StartupStage.StartupFailed + ":" + recordId.ToString("N")], calls);
    }

    [Fact]
    public void FailureAfterUsableUiRemainsOwnedByChild()
    {
        var stages = new List<string>();
        int captures = 0;
        var startup = new RuntimeStartupDiagnostics(
            (stage, category, id) => stages.Add(stage),
            (_, _, _) => { captures++; return Guid.NewGuid(); },
            Logger.None);

        startup.ReportUiReady();
        startup.ReportFailure(new InvalidOperationException(), "runtime");

        Assert.Equal(0, captures);
        Assert.Equal([StartupStage.UiReady, StartupStage.StartupFailed], stages);
    }

    [Fact]
    public async Task ReadinessWaitsForInitializationAndRenderedUi()
    {
        var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stages = new List<string>();
        var startup = new RuntimeStartupDiagnostics(
            (stage, _, _) => stages.Add(stage), (_, _, _) => null, Logger.None);

        Task observation = startup.ObserveInitializationAsync(() => initialized.Task, () => rendered.Task, () => true);
        Assert.Empty(stages);
        initialized.SetResult();
        Assert.Empty(stages);
        rendered.SetResult();
        await observation;

        Assert.Equal([StartupStage.UiReady], stages);
    }

    [Fact]
    public async Task ClosingDuringInitializationDoesNotReportReadiness()
    {
        var stages = new List<string>();
        var startup = new RuntimeStartupDiagnostics(
            (stage, _, _) => stages.Add(stage), (_, _, _) => null, Logger.None);

        await startup.ObserveInitializationAsync(() => Task.CompletedTask, () => Task.CompletedTask, () => false);

        Assert.Empty(stages);
    }

    [Fact]
    public void MissingEarlyConsentDoesNotConfigureRemoteService()
    {
        var startup = new RuntimeStartupDiagnostics(reporter: null, settings: null);
        var service = new RecordingDiagnosticsService();

        startup.InitializeRemoteDiagnostics(service);

        Assert.Empty(service.Configurations);
    }

    [Fact]
    public void EarlyOptOutPassesDestinationForPendingRecordPurgeAndStopsForwarding()
    {
        TelemetrySettings settings = CreateSettings() with { IsRemoteDiagnosticsEnabled = false };
        var startup = new RuntimeStartupDiagnostics(reporter: null, settings);
        var service = new RecordingDiagnosticsService();
        RemoteDiagnosticsSink.Clear();
        RemoteDiagnosticsSink.SetService(service);
        try
        {
            startup.InitializeRemoteDiagnostics(service);
            using var logger = new LoggerConfiguration().WriteTo.Sink(RemoteDiagnosticsSink.Instance).CreateLogger();
            logger.Information("Consent is disabled");

            var configuration = Assert.Single(service.Configurations);
            Assert.False(configuration.Options.IsEnabled);
            Assert.Equal(settings.InstallId, configuration.Options.InstallId);
            Assert.Equal(settings.HostUrl, configuration.Options.HostUrl);
            Assert.Equal(settings.ProjectToken, configuration.Options.ProjectToken);
            Assert.Empty(service.Events);
        }
        finally
        {
            RemoteDiagnosticsSink.Clear();
        }
    }

    [Fact]
    public void RuntimeInitializationKeepsUsingTheEarlyStartupService()
    {
        TelemetrySettings settings = CreateSettings();
        var startup = new RuntimeStartupDiagnostics(reporter: null, settings);
        var service = new RecordingDiagnosticsService();
        RemoteDiagnosticsSink.Clear();
        try
        {
            startup.InitializeRemoteDiagnostics(service);
            RemoteDiagnosticsContext context = Assert.Single(service.Configurations).Context;
            using var logger = new LoggerConfiguration().WriteTo.Sink(RemoteDiagnosticsSink.Instance).CreateLogger();
            logger.Information("Before configuration loaded");
            RemoteDiagnosticsLifecycle.Initialize(service, settings, new TelemetryContext(
                context.App, context.AppVersion, context.BuildConfiguration, context.Runtime,
                TelemetryRuntimePayloadSources.Unknown, TelemetryBootMediaTargets.None,
                context.RuntimeArchitecture, context.Locale, context.SessionId));
            logger.Information("After configuration loaded");

            Assert.Equal(2, service.Configurations.Count);
            Assert.Equal(["Before configuration loaded", "After configuration loaded"],
                service.Events.Select(entry => entry.RenderMessage()));
        }
        finally
        {
            RemoteDiagnosticsSink.Clear();
        }
    }

    [Fact]
    public void StartupFailureExchangeAndLocalLogShareOriginalEventIdentityAndContent()
    {
        var sink = new RecordingLogSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        LogEvent? captured = null;
        var startup = new RuntimeStartupDiagnostics((_, _, _) => { },
            (_, entry, _) => { captured = entry; return Guid.NewGuid(); }, logger);

        startup.ReportFailure(new InvalidOperationException("startup failure detail"), "configuration");

        LogEvent local = Assert.Single(sink.Events);
        Assert.NotNull(captured);
        Assert.Equal(captured.Properties["diagnostics.record_id"], local.Properties["diagnostics.record_id"]);
        Assert.Equal(captured.Properties["diagnostics.sequence"], local.Properties["diagnostics.sequence"]);
        Assert.Equal(captured.Timestamp, local.Timestamp);
        Assert.Equal(captured.RenderMessage(), local.RenderMessage());
        Assert.Equal(captured.Exception?.ToString(), local.Exception?.ToString());
    }

    private sealed class RecordingLogSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static TelemetrySettings CreateSettings() => new()
    {
        IsRemoteDiagnosticsEnabled = true,
        HostUrl = "https://posthog.example",
        ProjectToken = "test-project-token",
        InstallId = "test-install"
    };

    private sealed class RecordingDiagnosticsService : IRemoteDiagnosticsService
    {
        public List<(RemoteDiagnosticsOptions Options, RemoteDiagnosticsContext Context)> Configurations { get; } = [];
        public List<LogEvent> Events { get; } = [];
        public void Configure(RemoteDiagnosticsOptions options, RemoteDiagnosticsContext context) => Configurations.Add((options, context));
        public void Disable() { }
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
