// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Core;
using Serilog.Events;
using System.Diagnostics;

namespace Foundry.Telemetry;

/// <summary>
/// Provides the process-wide Serilog sink installed before runtime settings are available.
/// </summary>
public sealed class RemoteDiagnosticsSink : ILogEventSink
{
    private static IRemoteDiagnosticsService? _service;

    private RemoteDiagnosticsSink()
    {
    }

    /// <summary>
    /// Gets the process-wide delegating sink.
    /// </summary>
    public static RemoteDiagnosticsSink Instance { get; } = new();

    /// <summary>
    /// Registers the configured diagnostics service for subsequent log events.
    /// </summary>
    public static void SetService(IRemoteDiagnosticsService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        Volatile.Write(ref _service, service);
    }

    /// <summary>
    /// Removes the registered service. Intended for orderly shutdown and isolated tests.
    /// </summary>
    public static void Clear() => Volatile.Write(ref _service, null);

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        try
        {
            Volatile.Read(ref _service)?.Emit(logEvent);
        }
#pragma warning disable CA1031 // The delegating sink must never affect application logging.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Debug.WriteLine($"Remote diagnostics delegation failed: {ex.GetType().Name}");
        }
    }
}
