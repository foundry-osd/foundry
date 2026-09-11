// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Events;

namespace Foundry.Telemetry;

/// <summary>
/// Represents a prepared remote record; Logs and Error Tracking apply their own content policies.
/// </summary>
public sealed record RemoteDiagnosticRecord(
    DateTimeOffset Timestamp,
    LogEventLevel Level,
    string Body,
    IReadOnlyDictionary<string, object> Attributes,
    RemoteDiagnosticException? Exception)
{
    /// <summary>
    /// Allows repeated log context to retain its exception without duplicating Error Tracking events.
    /// </summary>
    internal bool ShouldTrackException { get; init; } = true;
}

/// <summary>
/// Represents a privacy-filtered exception chain.
/// </summary>
public sealed record RemoteDiagnosticException(
    string Type,
    string Message,
    string? StackTrace,
    IReadOnlyList<RemoteDiagnosticException> InnerExceptions);
