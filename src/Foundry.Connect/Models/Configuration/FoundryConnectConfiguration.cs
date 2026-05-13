using Foundry.Telemetry;

namespace Foundry.Connect.Models.Configuration;

/// <summary>
/// Represents the runtime configuration consumed by Foundry.Connect inside WinPE.
/// </summary>
public sealed class FoundryConnectConfiguration
{
    /// <summary>
    /// Gets the current configuration schema version.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Gets the schema version of this configuration.
    /// </summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// Gets capabilities of the generated media.
    /// </summary>
    public NetworkCapabilitiesOptions Capabilities { get; init; } = new();

    /// <summary>
    /// Gets wired 802.1X provisioning settings.
    /// </summary>
    public Dot1xSettings Dot1x { get; init; } = new();

    /// <summary>
    /// Gets Wi-Fi provisioning settings.
    /// </summary>
    public WifiSettings Wifi { get; init; } = new();

    /// <summary>
    /// Gets internet connectivity probe settings.
    /// </summary>
    public InternetProbeOptions InternetProbe { get; init; } = new();

    /// <summary>
    /// Gets telemetry policy and runtime settings for Foundry.Connect.
    /// </summary>
    public TelemetrySettings Telemetry { get; init; } = new();
}
