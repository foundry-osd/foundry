using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Diagnostics;
using Foundry.Connect.Models;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Models.Network;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Services.Network;

public sealed class NetworkStatusService : INetworkStatusService
{
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true
    });
    private static readonly TimeSpan WifiDiscoveryGracePeriod = TimeSpan.FromSeconds(15);

    private readonly FoundryConnectConfiguration _configuration;
    private readonly ILogger<NetworkStatusService> _logger;
    private IReadOnlyList<WifiNetworkSummary> _lastStableWifiNetworks = Array.Empty<WifiNetworkSummary>();
    private DateTimeOffset? _lastStableWifiNetworksAt;

    public NetworkStatusService(
        FoundryConnectConfiguration configuration,
        ILogger<NetworkStatusService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<NetworkStatusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        bool isDebugWifiEnabled = Debugger.IsAttached;
        NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(static adapter => adapter.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .ToArray();

        NetworkInterface[] ethernetAdapters = adapters.Where(IsEthernetAdapter).ToArray();
        NetworkInterface[] wirelessAdapters = adapters.Where(static adapter => adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211).ToArray();

        NetworkInterface? activeAdapter = ethernetAdapters.FirstOrDefault(static adapter => adapter.OperationalStatus == OperationalStatus.Up)
            ?? wirelessAdapters.FirstOrDefault(static adapter => adapter.OperationalStatus == OperationalStatus.Up)
            ?? ethernetAdapters.FirstOrDefault()
            ?? wirelessAdapters.FirstOrDefault();

        bool hasEthernetAdapter = ethernetAdapters.Length > 0;
        bool isEthernetConnected = ethernetAdapters.Any(static adapter => adapter.OperationalStatus == OperationalStatus.Up);
        bool hasWirelessAdapter = wirelessAdapters.Length > 0;
        bool isWifiUiEnabled = _configuration.Capabilities.WifiProvisioned || isDebugWifiEnabled;
        bool isWifiRuntimeAvailable = isWifiUiEnabled && await IsWifiRuntimeAvailableAsync(cancellationToken).ConfigureAwait(false);
        string? connectedWifiSsid = isWifiRuntimeAvailable ? NativeWifiApi.GetConnectedSsid() : null;
        IReadOnlyList<WifiNetworkSummary> wifiNetworks = isWifiRuntimeAvailable
            ? await DiscoverWifiNetworksAsync(cancellationToken).ConfigureAwait(false)
            : Array.Empty<WifiNetworkSummary>();
        bool hasDhcpLease = HasDhcpLease(activeAdapter);
        bool hasInternetAccess = await ProbeInternetAsync(cancellationToken).ConfigureAwait(false);

        IPInterfaceProperties? properties = activeAdapter?.GetIPProperties();
        UnicastIPAddressInformation? ipv4Information = properties?.UnicastAddresses
            .FirstOrDefault(static address => address.Address.AddressFamily == AddressFamily.InterNetwork);

        string adapterName = activeAdapter?.Name ?? "Unavailable";
        string ipAddress = ipv4Information?.Address.ToString() ?? "Unavailable";
        string subnetMask = ipv4Information?.IPv4Mask?.ToString() ?? "Unavailable";
        string gateway = properties?.GatewayAddresses
            .Select(static address => address.Address)
            .FirstOrDefault(static address => address.AddressFamily == AddressFamily.InterNetwork)?
            .ToString() ?? "Unavailable";
        string dnsServers = properties is null
            ? "Unavailable"
            : string.Join(", ",
                properties.DnsAddresses
                    .Where(static address => address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(static address => address.ToString()));

        if (string.IsNullOrWhiteSpace(dnsServers))
        {
            dnsServers = "Unavailable";
        }

        return new NetworkStatusSnapshot
        {
            LayoutMode = isWifiRuntimeAvailable ? NetworkLayoutMode.EthernetWifi : NetworkLayoutMode.EthernetOnly,
            HasInternetAccess = hasInternetAccess,
            HasEthernetAdapter = hasEthernetAdapter,
            IsEthernetConnected = isEthernetConnected,
            HasDhcpLease = hasDhcpLease,
            IsWifiRuntimeAvailable = isWifiRuntimeAvailable,
            HasWirelessAdapter = hasWirelessAdapter,
            EthernetStatusText = BuildEthernetStatusText(hasEthernetAdapter, isEthernetConnected, hasDhcpLease),
            InternetStatusText = hasInternetAccess
                ? "Internet reachability validated."
                : "Internet validation is still pending or failed.",
            AdapterName = adapterName,
            IpAddress = ipAddress,
            SubnetMask = subnetMask,
            GatewayAddress = gateway,
            DnsServers = dnsServers,
            DhcpText = hasDhcpLease ? "DHCP lease detected." : "No DHCP lease detected.",
            ConnectionSummary = BuildConnectionSummary(isEthernetConnected, hasInternetAccess, isWifiRuntimeAvailable, wifiNetworks.Count),
            ConnectedWifiSsid = connectedWifiSsid,
            WifiNetworks = wifiNetworks
        };
    }

    private async Task<bool> ProbeInternetAsync(CancellationToken cancellationToken)
    {
        foreach (string probeUri in _configuration.InternetProbe.ProbeUris)
        {
            using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(_configuration.InternetProbe.TimeoutSeconds));

            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get, probeUri);
                using HttpResponseMessage response = await HttpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Internet probe timed out for {ProbeUri}.", probeUri);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Internet probe failed for {ProbeUri}.", probeUri);
            }
        }

        return false;
    }

    private Task<bool> IsWifiRuntimeAvailableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return Task.FromResult(NativeWifiApi.IsRuntimeAvailable());
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Native Wi-Fi runtime is unavailable.");
            return Task.FromResult(false);
        }
    }

    private Task<IReadOnlyList<WifiNetworkSummary>> DiscoverWifiNetworksAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            IReadOnlyList<WifiNetworkSummary> networks = NativeWifiApi.GetAvailableNetworks();
            if (networks.Count > 0)
            {
                _lastStableWifiNetworks = networks;
                _lastStableWifiNetworksAt = DateTimeOffset.UtcNow;
                return Task.FromResult(networks);
            }

            if (_lastStableWifiNetworks.Count > 0 &&
                _lastStableWifiNetworksAt is DateTimeOffset lastStableWifiNetworksAt &&
                DateTimeOffset.UtcNow - lastStableWifiNetworksAt <= WifiDiscoveryGracePeriod)
            {
                _logger.LogDebug(
                    "Native Wi-Fi discovery returned no networks. Reusing {WifiNetworkCount} cached network(s) from {DiscoveredAt}.",
                    _lastStableWifiNetworks.Count,
                    lastStableWifiNetworksAt);
                return Task.FromResult(_lastStableWifiNetworks);
            }

            return Task.FromResult<IReadOnlyList<WifiNetworkSummary>>(Array.Empty<WifiNetworkSummary>());
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Native Wi-Fi network discovery failed.");
            return Task.FromResult<IReadOnlyList<WifiNetworkSummary>>(Array.Empty<WifiNetworkSummary>());
        }
    }

    private static bool IsEthernetAdapter(NetworkInterface adapter)
    {
        return adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet
            or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.FastEthernetFx
            or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.Ethernet3Megabit;
    }

    private static bool HasDhcpLease(NetworkInterface? adapter)
    {
        try
        {
            return adapter?.GetIPProperties().GetIPv4Properties()?.IsDhcpEnabled == true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildEthernetStatusText(bool hasEthernetAdapter, bool isEthernetConnected, bool hasDhcpLease)
    {
        if (!hasEthernetAdapter)
        {
            return "No ethernet adapter detected.";
        }

        if (!isEthernetConnected)
        {
            return "Ethernet adapter detected, but no active link is available.";
        }

        return hasDhcpLease
            ? "Ethernet link is active and DHCP information is available."
            : "Ethernet link is active, but no DHCP lease was detected.";
    }

    private static string BuildConnectionSummary(bool isEthernetConnected, bool hasInternetAccess, bool isWifiRuntimeAvailable, int wifiNetworkCount)
    {
        if (hasInternetAccess)
        {
            return "Bootstrap can continue when the countdown completes.";
        }

        if (isEthernetConnected)
        {
            return "Ethernet is connected. Waiting for internet validation to succeed.";
        }

        if (isWifiRuntimeAvailable && wifiNetworkCount > 0)
        {
            return "Wi-Fi networks are available. Internet validation is still pending.";
        }

        return "Waiting for an active network path.";
    }
}
