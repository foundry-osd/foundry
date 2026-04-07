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

    public string InternetStatusText { get; init; } = "Internet validation has not succeeded yet.";

    public string AdapterName { get; init; } = "Unavailable";

    public string IpAddress { get; init; } = "Unavailable";

    public string SubnetMask { get; init; } = "Unavailable";

    public string GatewayAddress { get; init; } = "Unavailable";

    public string DnsServers { get; init; } = "Unavailable";

    public string DhcpText { get; init; } = "Unavailable";

    public string ConnectionSummary { get; init; } = "Waiting for a validated network path.";

    public string? ConnectedWifiSsid { get; init; }

    public IReadOnlyList<WifiNetworkSummary> WifiNetworks { get; init; } = Array.Empty<WifiNetworkSummary>();
}
