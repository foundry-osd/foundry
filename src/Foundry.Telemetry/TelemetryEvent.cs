// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Telemetry;

/// <summary>
/// Represents a sanitized telemetry event ready for transport.
/// </summary>
/// <param name="Name">Stable event name.</param>
/// <param name="Properties">Event properties after allowlist filtering.</param>
public sealed record TelemetryEvent(
    string Name,
    IReadOnlyDictionary<string, object?> Properties);
