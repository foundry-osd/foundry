// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;
using Foundry.Core.Tests.WinPe;
using Foundry.Telemetry;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Foundry.Core.Tests.Runtime;

[Collection(WinPeProcessLoggingCollection.Name)]
public sealed class HostedPhaseScopeTests
{
    private static readonly RuntimeApplicationIdentity Identity = new("Foundry.Connect", TelemetryApps.FoundryConnect, "1.0.0.0");

    [Fact]
    public async Task BeginAndEnd_SwapTheProcessLoggerAndTearDownInOrder()
    {
        ILogger previous = Log.Logger;
        var sink = new RecordingSink();
        var steps = new List<string>();
        Logger phaseLogger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        try
        {
            HostedPhaseScope scope = HostedPhaseScope.Begin(
                Identity, "Foundry Connect", typeof(HostedPhaseScopeTests), static () => @"C:\Logs\FoundryConnect.log", _ => phaseLogger);

            Assert.Same(phaseLogger, Log.Logger);
            Assert.Equal(@"C:\Logs\FoundryConnect.log", scope.LogFilePath);

            await scope.EndAsync(new RecordingTelemetry(steps), new RecordingServices(steps));
            await scope.EndAsync(new RecordingTelemetry(steps), new RecordingServices(steps));

            Assert.Equal(["flush", "dispose"], steps);
            Assert.Same(previous, Log.Logger);
            Assert.Contains(sink.Events, static entry => entry.RenderMessage().Contains("phase shutdown completed", StringComparison.Ordinal));
        }
        finally
        {
            Log.Logger = previous;
        }
    }

    [Fact]
    public async Task Begin_WhenTheFileLoggerFails_FallsBackAndEndRestoresTheLogger()
    {
        ILogger previous = Log.Logger;
        try
        {
            HostedPhaseScope scope = HostedPhaseScope.Begin(
                Identity, "Foundry Connect", typeof(HostedPhaseScopeTests),
                static () => throw new IOException("The log directory is not writable."), static _ => throw new InvalidOperationException());

            Assert.Equal("<unavailable>", scope.LogFilePath);
            Assert.NotSame(previous, Log.Logger);

            await scope.EndAsync(telemetry: null, services: null);

            Assert.Same(previous, Log.Logger);
        }
        finally
        {
            Log.Logger = previous;
        }
    }

    [Fact]
    public async Task EndAsync_WhenTeardownStepsFail_StillRestoresTheLoggerWithoutThrowing()
    {
        ILogger previous = Log.Logger;
        try
        {
            HostedPhaseScope scope = HostedPhaseScope.Begin(
                Identity, "Foundry Connect", typeof(HostedPhaseScopeTests), static () => "phase.log", static _ => Logger.None);

            await scope.EndAsync(new FailingTelemetry(), new FailingServices());

            Assert.Same(previous, Log.Logger);
        }
        finally
        {
            Log.Logger = previous;
        }
    }

    private sealed class RecordingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class RecordingTelemetry(List<string> steps) : ITelemetryService
    {
        public Task TrackAsync(string eventName, IReadOnlyDictionary<string, object?> properties, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            steps.Add("flush");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingServices(List<string> steps) : IDisposable
    {
        public void Dispose() => steps.Add("dispose");
    }

    private sealed class FailingTelemetry : ITelemetryService
    {
        public Task TrackAsync(string eventName, IReadOnlyDictionary<string, object?> properties, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task FlushAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Flush failed.");
    }

    private sealed class FailingServices : IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("Disposal failed.");
    }
}
