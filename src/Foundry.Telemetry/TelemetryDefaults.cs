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
    /// <remarks>
    /// Version 3 keeps property names and sends canonical values: snake_case enum values, one cancelled failure
    /// vocabulary, lowercase failure codes, normalized tool names, canonical Windows edition names, and only stable
    /// media step names.
    /// </remarks>
    public const int SchemaVersion = 3;

    /// <summary>
    /// Gets the PostHog EU ingestion host used by official Foundry telemetry.
    /// </summary>
    public const string PostHogEuHost = "https://eu.i.posthog.com";

    /// <summary>
    /// Gets the public PostHog project token injected by official release builds.
    /// </summary>
    public static string ProjectToken => TelemetryProjectToken.Value;
}
