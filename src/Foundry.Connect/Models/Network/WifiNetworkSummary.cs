namespace Foundry.Connect.Models.Network;

/// <summary>
/// Represents a Wi-Fi network discovered from the native WLAN API.
/// </summary>
public sealed class WifiNetworkSummary
{
    /// <summary>
    /// Gets the display SSID or a hidden-network placeholder.
    /// </summary>
    public string Ssid { get; init; } = "Hidden network";

    /// <summary>
    /// Gets the raw SSID bytes encoded as hexadecimal when available.
    /// </summary>
    public string? SsidHex { get; init; }

    /// <summary>
    /// Gets signal strength as a percentage.
    /// </summary>
    public int SignalStrengthPercent { get; init; }

    /// <summary>
    /// Gets the authentication algorithm description.
    /// </summary>
    public string Authentication { get; init; } = "Unknown";

    /// <summary>
    /// Gets the encryption algorithm description.
    /// </summary>
    public string Encryption { get; init; } = "Unknown";
}
