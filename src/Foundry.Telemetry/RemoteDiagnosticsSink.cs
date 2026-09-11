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
    private static readonly object Gate = new();
    private static readonly Queue<LogEvent> Startup = new();
    private static bool _bufferStartup = true;
    private static long _startupDropped;
    internal static string? LogDirectory { get; private set; }

    /// <summary>Sets the process-local outbox root beside the selected local logs, before diagnostics initialization.</summary>
    public static void SetLogDirectory(string? root) => LogDirectory = root;

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
        lock (Gate)
        {
            Volatile.Write(ref _service, service);
            while (Startup.TryDequeue(out LogEvent? logEvent)) service.Emit(logEvent);
            _bufferStartup = false;
        }
        long dropped = Interlocked.Exchange(ref _startupDropped, 0);
        if (dropped > 0)
            Serilog.Log.ForContext("PostHogTransportInternal", true).Warning(
                "Startup log buffer overflowed before diagnostic settings were available. LostRecords={LostRecords}", dropped);
    }

    /// <summary>
    /// Removes the registered service. Intended for orderly shutdown and isolated tests.
    /// </summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Volatile.Write(ref _service, null);
            Startup.Clear();
            _startupDropped = 0;
            _bufferStartup = false;
        }
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        try
        {
            lock (Gate)
            {
                if (_service is not null) _service.Emit(logEvent);
                else if (_bufferStartup)
                {
                    if (Startup.Count == DurableLogQueue.DefaultMaximumRecords)
                    {
                        Startup.Dequeue();
                        _startupDropped++;
                    }
                    Startup.Enqueue(logEvent);
                }
            }
        }
#pragma warning disable CA1031 // The delegating sink must never affect application logging.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Debug.WriteLine($"Remote diagnostics delegation failed: {ex.GetType().Name}");
        }
    }
}
