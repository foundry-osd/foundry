// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Telemetry;

/// <summary>
/// Defines stable telemetry constants shared by Foundry OSD, Foundry.Connect, and Foundry.Deploy.
/// </summary>
public static class TelemetryDefaults
{
    /// <summary>
    /// Gets the current telemetry event contract version.
    /// </summary>
    public const int SchemaVersion = 2;

    /// <summary>
    /// Gets the PostHog EU ingestion host used by official Foundry telemetry.
    /// </summary>
    public const string PostHogEuHost = "https://eu.i.posthog.com";

    /// <summary>
    /// Gets the public PostHog project token injected by official release builds.
    /// </summary>
    public static string ProjectToken => TelemetryProjectToken.Value;
}
