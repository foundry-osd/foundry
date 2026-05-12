using Foundry.Connect.Models;

namespace Foundry.Connect.Models.Network;

/// <summary>
/// Represents a point-in-time network status snapshot displayed by Foundry.Connect.
/// </summary>
public sealed class NetworkStatusSnapshot
{
    /// <summary>
    /// Gets the layout mode selected from detected network capabilities.
    /// </summary>
    public NetworkLayoutMode LayoutMode { get; init; }

    /// <summary>
    /// Gets whether an internet probe succeeded.
    /// </summary>
    public bool HasInternetAccess { get; init; }

    /// <summary>
    /// Gets whether any Ethernet adapter was detected.
    /// </summary>
    public bool HasEthernetAdapter { get; init; }

    /// <summary>
    /// Gets a value indicating whether an Ethernet adapter currently has link.
    /// </summary>
    public bool IsEthernetConnected { get; init; }

    /// <summary>
    /// Gets a value indicating whether the connected Ethernet adapter uses DHCP.
    /// </summary>
    public bool HasDhcpLease { get; init; }

    /// <summary>
    /// Gets a value indicating whether an Ethernet IPv4 address is available.
    /// </summary>
    public bool HasEthernetIpv4 { get; init; }

    /// <summary>
    /// Gets a value indicating whether Wi-Fi controls can use the native WLAN runtime.
    /// </summary>
    public bool IsWifiRuntimeAvailable { get; init; }

    /// <summary>
    /// Gets a value indicating whether at least one wireless adapter was detected.
    /// </summary>
    public bool HasWirelessAdapter { get; init; }

    /// <summary>
    /// Gets the primary Ethernet status text shown by the UI.
    /// </summary>
    public string EthernetStatusText { get; init; } = string.Empty;

    /// <summary>
    /// Gets the secondary Ethernet status text shown by the UI.
    /// </summary>
    public string EthernetSecondaryStatusText { get; init; } = string.Empty;

    /// <summary>
    /// Gets the display name of the Ethernet adapter used for status details.
    /// </summary>
    public string EthernetAdapterName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Ethernet IPv4 address shown by the UI.
    /// </summary>
    public string EthernetIpAddress { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Ethernet gateway address shown by the UI.
    /// </summary>
    public string EthernetGateway { get; init; } = string.Empty;

    /// <summary>
    /// Gets the currently connected Wi-Fi SSID when the WLAN runtime reports one.
    /// </summary>
    public string? ConnectedWifiSsid { get; init; }

    /// <summary>
    /// Gets the discovered Wi-Fi networks.
    /// </summary>
    public IReadOnlyList<WifiNetworkSummary> WifiNetworks { get; init; } = Array.Empty<WifiNetworkSummary>();
}
