// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Events;

namespace Foundry.Telemetry;

/// <summary>
/// Represents a privacy-filtered log record that is safe to enqueue for remote delivery.
/// </summary>
public sealed record RemoteDiagnosticRecord(
    DateTimeOffset Timestamp,
    LogEventLevel Level,
    string Body,
    IReadOnlyDictionary<string, object> Attributes,
    RemoteDiagnosticException? Exception);

/// <summary>
/// Represents a privacy-filtered exception chain.
/// </summary>
public sealed record RemoteDiagnosticException(
    string Type,
    string Message,
    string? StackTrace,
    IReadOnlyList<RemoteDiagnosticException> InnerExceptions);
