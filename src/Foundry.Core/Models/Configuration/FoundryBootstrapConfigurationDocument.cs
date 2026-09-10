// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Telemetry;

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Contains only the telemetry policy consumed by the boot orchestrator.
/// </summary>
public sealed record FoundryBootstrapConfigurationDocument
{
    /// <summary>
    /// Defines the independently versioned Bootstrap configuration contract.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Gets the schema version of this configuration document.
    /// </summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// Gets telemetry settings, disabled unless authoring explicitly supplies consent.
    /// </summary>
    public TelemetrySettings Telemetry { get; init; } = new()
    {
        IsEnabled = false,
        IsRemoteDiagnosticsEnabled = false
    };
}
