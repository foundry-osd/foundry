namespace Foundry.Connect.Models.Network;

public sealed class WifiNetworkSummary
{
    public string Ssid { get; init; } = "Hidden network";

    public string? SsidHex { get; init; }

    public int SignalStrengthPercent { get; init; }

    public string Authentication { get; init; } = "Unknown";

    public string Encryption { get; init; } = "Unknown";
}
