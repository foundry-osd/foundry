// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Telemetry;

/// <summary>
/// Provides stable, low-cardinality context for remote diagnostic records.
/// </summary>
public sealed record RemoteDiagnosticsContext(
    string App,
    string AppVersion,
    string BuildConfiguration,
    string Runtime,
    string RuntimeArchitecture,
    string Locale,
    string SessionId,
    string Release);
