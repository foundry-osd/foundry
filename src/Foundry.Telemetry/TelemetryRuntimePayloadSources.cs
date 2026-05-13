namespace Foundry.Telemetry;

/// <summary>
/// Defines stable categories for the Connect and Deploy runtime payload source.
/// </summary>
public static class TelemetryRuntimePayloadSources
{
    /// <summary>
    /// Indicates that no runtime payload source applies to the current app.
    /// </summary>
    public const string None = "none";

    /// <summary>
    /// Indicates a runtime payload provisioned from OSD debug-local artifacts.
    /// </summary>
    public const string Debug = "debug";

    /// <summary>
    /// Indicates a runtime payload provisioned from release artifacts.
    /// </summary>
    public const string Release = "release";

    /// <summary>
    /// Indicates that the runtime payload source could not be resolved safely.
    /// </summary>
    public const string Unknown = "unknown";
}
