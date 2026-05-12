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

    public bool IsEthernetConnected { get; init; }

    public bool HasDhcpLease { get; init; }

    public bool HasEthernetIpv4 { get; init; }

    public bool IsWifiRuntimeAvailable { get; init; }

    public bool HasWirelessAdapter { get; init; }

    public string EthernetStatusText { get; init; } = string.Empty;

    public string EthernetSecondaryStatusText { get; init; } = string.Empty;

    public string EthernetAdapterName { get; init; } = string.Empty;

    public string EthernetIpAddress { get; init; } = string.Empty;

    public string EthernetGateway { get; init; } = string.Empty;

    public string? ConnectedWifiSsid { get; init; }

    /// <summary>
    /// Gets the discovered Wi-Fi networks.
    /// </summary>
    public IReadOnlyList<WifiNetworkSummary> WifiNetworks { get; init; } = Array.Empty<WifiNetworkSummary>();
}
