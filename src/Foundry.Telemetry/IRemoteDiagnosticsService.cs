// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Events;

namespace Foundry.Telemetry;

/// <summary>
/// Receives application log events and manages the remote diagnostics lifecycle.
/// </summary>
public interface IRemoteDiagnosticsService : IAsyncDisposable
{
    /// <summary>
    /// Configures remote diagnostics after settings and runtime context are available.
    /// </summary>
    void Configure(RemoteDiagnosticsOptions options, RemoteDiagnosticsContext context);

    /// <summary>
    /// Attempts to accept a log event without blocking the caller.
    /// </summary>
    void Emit(LogEvent logEvent);

    /// <summary>
    /// Stops accepting records and drains buffered diagnostics within the cancellation budget.
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken);
}

internal interface IRemoteDiagnosticsExporter : IAsyncDisposable
{
    ValueTask ExportAsync(RemoteDiagnosticRecord record, CancellationToken cancellationToken);

    Task FlushAsync(CancellationToken cancellationToken);
}
