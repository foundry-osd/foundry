// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Events;
using Foundry.Telemetry;

namespace Foundry.Telemetry.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RemoteDiagnosticsSinkCollection
{
    public const string Name = "RemoteDiagnosticsSink";
}

[Collection(RemoteDiagnosticsSinkCollection.Name)]
public sealed class RemoteDiagnosticsSinkTests : IDisposable
{
    [Fact]
    public void Emit_BeforeServiceRegistration_IsNoOp()
    {
        RemoteDiagnosticsSink.Clear();

        Exception? exception = Record.Exception(() =>
            RemoteDiagnosticsSink.Instance.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "Failed")));

        Assert.Null(exception);
    }

    [Fact]
    public void Emit_AfterServiceRegistration_DelegatesOnce()
    {
        var service = new RecordingRemoteDiagnosticsService();
        RemoteDiagnosticsSink.SetService(service);
        LogEvent logEvent = RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "Failed");

        RemoteDiagnosticsSink.Instance.Emit(logEvent);

        Assert.Same(logEvent, Assert.Single(service.Events));
    }

    [Fact]
    public void Emit_WhenRegisteredServiceThrows_DoesNotThrow()
    {
        RemoteDiagnosticsSink.SetService(new ThrowingRemoteDiagnosticsService());

        Exception? exception = Record.Exception(() =>
            RemoteDiagnosticsSink.Instance.Emit(RemoteDiagnosticsTestData.LogEvent(LogEventLevel.Error, "Failed")));

        Assert.Null(exception);
    }

    public void Dispose() => RemoteDiagnosticsSink.Clear();

    private sealed class RecordingRemoteDiagnosticsService : IRemoteDiagnosticsService
    {
        public List<LogEvent> Events { get; } = [];

        public void Configure(RemoteDiagnosticsOptions options, RemoteDiagnosticsContext context)
        {
        }

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);

        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingRemoteDiagnosticsService : IRemoteDiagnosticsService
    {
        public void Configure(RemoteDiagnosticsOptions options, RemoteDiagnosticsContext context)
        {
        }

        public void Emit(LogEvent logEvent) => throw new InvalidOperationException("failed");

        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
