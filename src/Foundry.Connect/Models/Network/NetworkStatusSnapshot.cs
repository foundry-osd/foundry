using Foundry.Connect.Models;

namespace Foundry.Connect.Models.Network;

public sealed class NetworkStatusSnapshot
{
    public NetworkLayoutMode LayoutMode { get; init; }

    public bool HasInternetAccess { get; init; }

    public bool HasEthernetAdapter { get; init; }

    public bool IsEthernetConnected { get; init; }

    public bool HasDhcpLease { get; init; }

    public bool IsWifiRuntimeAvailable { get; init; }

    public bool HasWirelessAdapter { get; init; }

    public string EthernetStatusText { get; init; } = "No ethernet adapter detected.";

    public string EthernetAdapterName { get; init; } = "Unavailable";

    public string EthernetIpAddress { get; init; } = "Unavailable";

    public string? ConnectedWifiSsid { get; init; }

    public IReadOnlyList<WifiNetworkSummary> WifiNetworks { get; init; } = Array.Empty<WifiNetworkSummary>();
}
