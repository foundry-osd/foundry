// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Models.Diagnostics;

public sealed record ConnectDiagnosticsSnapshot(
    string ApplicationVersion,
    string RuntimeIdentifier,
    string ProcessArchitecture,
    string ConfigurationSource,
    TimeSpan RefreshInterval,
    DateTimeOffset? LastUpdated,
    string ReadinessState,
    string? ActiveConnectionSource,
    IReadOnlyList<string> AdapterSummaries,
    string? LastError,
    DateTimeOffset CapturedAt);
