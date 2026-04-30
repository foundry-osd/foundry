namespace Foundry.Models.Configuration;

public sealed record FoundryConnectConfigurationDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public ConnectNetworkCapabilitiesSettings Capabilities { get; init; } = new();

    public Dot1xSettings Dot1x { get; init; } = new();

    public WifiSettings Wifi { get; init; } = new();

    public ConnectInternetProbeSettings InternetProbe { get; init; } = new();
}
