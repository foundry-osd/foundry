namespace Foundry.Telemetry;

/// <summary>
/// Defines stable telemetry constants shared by Foundry OSD, Foundry.Connect, and Foundry.Deploy.
/// </summary>
public static class TelemetryDefaults
{
    /// <summary>
    /// Gets the current telemetry event contract version.
    /// </summary>
    public const int SchemaVersion = 1;

    /// <summary>
    /// Gets the PostHog EU ingestion host used by official Foundry telemetry.
    /// </summary>
    public const string PostHogEuHost = "https://eu.i.posthog.com";

    /// <summary>
    /// Gets the public PostHog project token injected by official release builds.
    /// </summary>
    public static string ProjectToken => TelemetryProjectToken.Value;
}
