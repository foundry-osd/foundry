// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Serilog.Events;

namespace Foundry.Utilities.Diagnostics;

/// <summary>Captures boot-clock reliability on events without rewriting their original timestamps.</summary>
public sealed class DiagnosticClock
{
    public const string EnvironmentVariableName = "FOUNDRY_DIAGNOSTIC_CLOCK_SYNCHRONIZED";
    public const string SynchronizedProperty = "diagnostics.clock_synchronized";
    public const string OriginalTimestampProperty = "diagnostics.original_timestamp";
    public const string TimestampSourceProperty = "diagnostics.timestamp_source";
    private int _state;

    /// <summary>The clock policy for this process; desktop logging keeps its usual timestamps by default.</summary>
    public static DiagnosticClock Current { get; } = new();

    /// <summary>Null outside managed boot logging, otherwise the current verified clock state.</summary>
    public bool? IsSynchronized => Volatile.Read(ref _state) switch { 0 => null, 2 => true, _ => false };

    /// <summary>Starts boot logging, accepting only an explicit synchronized state inherited from Bootstrap.</summary>
    public void InitializeRuntime(string? inheritedState) =>
        Volatile.Write(ref _state, string.Equals(inheritedState, "true", StringComparison.OrdinalIgnoreCase) ? 2 : 1);

    /// <summary>Marks subsequent events after successful UTC verification or correction.</summary>
    public void MarkSynchronized() => Volatile.Write(ref _state, 2);

    /// <summary>Snapshots reliability once, preserving prior state when events are normalized again.</summary>
    public void Enrich(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        if (IsSynchronized is bool synchronized)
            logEvent.AddPropertyIfAbsent(new LogEventProperty(SynchronizedProperty, new ScalarValue(synchronized)));
        if (logEvent.Properties.TryGetValue(SynchronizedProperty, out LogEventPropertyValue? value) &&
            value is ScalarValue { Value: bool capturedState })
        {
            logEvent.AddPropertyIfAbsent(new LogEventProperty(TimestampSourceProperty,
                new ScalarValue(capturedState ? "event" : "ingestion")));
            if (!capturedState)
                logEvent.AddPropertyIfAbsent(new LogEventProperty(OriginalTimestampProperty,
                    new ScalarValue(logEvent.Timestamp.ToString("O", CultureInfo.InvariantCulture))));
        }
    }
}
