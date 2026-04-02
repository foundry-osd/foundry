namespace Foundry.Connect.Models.Configuration;

public sealed class FoundryConnectConfiguration
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public NetworkCapabilitiesOptions Capabilities { get; init; } = new();

    public Dot1xSettings Dot1x { get; init; } = new();

    public WifiSettings Wifi { get; init; } = new();

    public InternetProbeOptions InternetProbe { get; init; } = new();
}
