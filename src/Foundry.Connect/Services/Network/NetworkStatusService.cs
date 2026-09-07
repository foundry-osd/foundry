// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Diagnostics;
using Foundry.Connect.Models;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Services.Localization;
using Foundry.Connect.Models.Network;
using Foundry.Utilities.Networking;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Services.Network;

/// <summary>
/// Builds network status snapshots from Windows adapter state, native WLAN APIs, and configured Internet probes.
/// </summary>
public sealed class NetworkStatusService : INetworkStatusService
{
    private static readonly TimeSpan WifiDiscoveryGracePeriod = TimeSpan.FromSeconds(15);

    private readonly INetworkProbeService _probeService;
    private readonly FoundryConnectConfiguration _configuration;
    private readonly ILocalizationService _localizationService;
    private readonly INetworkAdapterSnapshotProvider _networkAdapterSnapshotProvider;
    private readonly ILogger<NetworkStatusService> _logger;
    private IReadOnlyList<WifiNetworkSummary> _lastStableWifiNetworks = Array.Empty<WifiNetworkSummary>();
    private DateTimeOffset? _lastStableWifiNetworksAt;

    public NetworkStatusService(
        FoundryConnectConfiguration configuration,
        ILocalizationService localizationService,
        ILogger<NetworkStatusService> logger,
        INetworkProbeService probeService,
        INetworkAdapterSnapshotProvider? networkAdapterSnapshotProvider = null)
    {
        _probeService = probeService;
        _configuration = configuration;
        _localizationService = localizationService;
        _logger = logger;
        _networkAdapterSnapshotProvider = networkAdapterSnapshotProvider ?? new WindowsNetworkAdapterSnapshotProvider();
    }

    public async Task<NetworkStatusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool isDebugWifiEnabled = Debugger.IsAttached;
        NetworkAdapterSnapshot[] adapters = _networkAdapterSnapshotProvider.GetAdapters()
            .Where(static adapter => adapter.InterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .ToArray();

        NetworkAdapterSnapshot[] ethernetAdapters = adapters.Where(IsEthernetAdapter).ToArray();
        NetworkAdapterSnapshot[] wirelessAdapters = adapters.Where(static adapter => adapter.InterfaceType == NetworkInterfaceType.Wireless80211).ToArray();

        NetworkAdapterSnapshot? connectedEthernetAdapter = ethernetAdapters.FirstOrDefault(static adapter => adapter.OperationalStatus == OperationalStatus.Up);
        NetworkAdapterSnapshot? ethernetDisplayAdapter = connectedEthernetAdapter ?? ethernetAdapters.FirstOrDefault();

        bool hasEthernetAdapter = ethernetAdapters.Length > 0;
        bool isEthernetConnected = ethernetAdapters.Any(static adapter => adapter.OperationalStatus == OperationalStatus.Up);
        bool hasWirelessAdapter = wirelessAdapters.Length > 0;
        bool isWifiUiEnabled = _configuration.Capabilities.WifiProvisioned || isDebugWifiEnabled;
        bool isWifiRuntimeAvailable = isWifiUiEnabled && await IsWifiRuntimeAvailableAsync(cancellationToken).ConfigureAwait(false);
        var connectedWifi = isWifiRuntimeAvailable ? NativeWifiApi.GetConnectedNetwork() : null;
        IReadOnlyList<WifiNetworkSummary> wifiNetworks = isWifiRuntimeAvailable
            ? await DiscoverWifiNetworksAsync(cancellationToken).ConfigureAwait(false)
            : Array.Empty<WifiNetworkSummary>();
        bool isDhcpEnabled = connectedEthernetAdapter?.IsDhcpEnabled == true;
        NetworkAdapterSnapshot[] linkedAdapters = adapters.Where(static adapter => adapter.OperationalStatus == OperationalStatus.Up).ToArray();
        NetworkProbeResult readiness = linkedAdapters.Length == 0
            ? new(NetworkReadinessStatus.NoLink, "network_no_link")
            : !linkedAdapters.Any(HasUsableAddress)
                ? new(NetworkReadinessStatus.NoUsableAddress, "network_no_usable_address")
                : await _probeService.ProbeAsync(cancellationToken).ConfigureAwait(false);

        NetworkIpv4AddressSnapshot? ethernetIpv4Information = ethernetDisplayAdapter?.Ipv4Addresses.FirstOrDefault(address => IsUsableAddress(address.Address));
        string? ethernetGateway = connectedEthernetAdapter?.Gateways.FirstOrDefault();
        bool hasEthernetIpv4 = ethernetDisplayAdapter?.Ipv4Addresses.Any(address => IsUsableAddress(address.Address)) == true;
        bool hasEthernetUsableAddress = ethernetDisplayAdapter is not null && HasUsableAddress(ethernetDisplayAdapter);

        return new NetworkStatusSnapshot
        {
            LayoutMode = isWifiRuntimeAvailable ? NetworkLayoutMode.EthernetWifi : NetworkLayoutMode.EthernetOnly,
            Readiness = readiness,
            HasEthernetAdapter = hasEthernetAdapter,
            IsEthernetConnected = isEthernetConnected,
            IsDhcpEnabled = isDhcpEnabled,
            HasEthernetIpv4 = hasEthernetIpv4,
            HasEthernetUsableAddress = hasEthernetUsableAddress,
            IsWifiRuntimeAvailable = isWifiRuntimeAvailable,
            HasWirelessAdapter = hasWirelessAdapter,
            EthernetStatusText = BuildEthernetStatusText(hasEthernetAdapter, isEthernetConnected, hasEthernetUsableAddress),
            EthernetSecondaryStatusText = BuildEthernetSecondaryStatusText(hasEthernetAdapter, isEthernetConnected, hasEthernetUsableAddress, isDhcpEnabled),
            EthernetAdapterName = ethernetDisplayAdapter?.Name ?? GetString("Common.Unavailable"),
            EthernetIpAddress = ethernetIpv4Information?.Address ?? ethernetDisplayAdapter?.Ipv6Addresses.FirstOrDefault(IsUsableAddress)
                ?? ethernetDisplayAdapter?.Ipv4Addresses.FirstOrDefault()?.Address ?? GetString("Common.Unavailable"),
            EthernetGateway = ethernetGateway ?? GetString("Common.Unavailable"),
            ConnectedWifiSsid = connectedWifi?.CurrentSsid,
            ConnectedWifiSsidHex = connectedWifi?.CurrentSsidHex,
            WifiNetworks = wifiNetworks
        };
    }

    private static bool HasUsableAddress(NetworkAdapterSnapshot adapter) =>
        adapter.Ipv4Addresses.Any(address => IsUsableAddress(address.Address)) || adapter.Ipv6Addresses.Any(IsUsableAddress);

    internal static bool IsUsableAddress(string value)
    {
        if (!IPAddress.TryParse(value, out IPAddress? address) || IPAddress.IsLoopback(address)) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = address.GetAddressBytes();
            return bytes[0] is not (0 or 127) && bytes[0] < 224 && !(bytes[0] == 169 && bytes[1] == 254);
        }
        return address.AddressFamily == AddressFamily.InterNetworkV6 && !address.Equals(IPAddress.IPv6Any) &&
            !IPAddress.IsLoopback(address) && !address.IsIPv6LinkLocal && !address.IsIPv6Multicast && !address.IsIPv6SiteLocal;
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

    private static bool IsEthernetAdapter(NetworkAdapterSnapshot adapter)
    {
        return adapter.InterfaceType is NetworkInterfaceType.Ethernet
            or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.FastEthernetFx
            or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.Ethernet3Megabit;
    }

    private string BuildEthernetStatusText(bool hasEthernetAdapter, bool isEthernetConnected, bool hasEthernetIpv4)
    {
        if (!hasEthernetAdapter)
        {
            return GetString("Ethernet.NoAdapterDetected");
        }

        if (!isEthernetConnected)
        {
            return GetString("Ethernet.NoActiveLink");
        }

        return hasEthernetIpv4
            ? GetString("Common.Connected")
            : GetString("Ethernet.WaitingConfiguration");
    }

    private string BuildEthernetSecondaryStatusText(bool hasEthernetAdapter, bool isEthernetConnected, bool hasEthernetIpv4, bool isDhcpEnabled)
    {
        if (!hasEthernetAdapter)
        {
            return string.Empty;
        }

        if (!isEthernetConnected)
        {
            return GetString("Ethernet.CheckCable");
        }

        if (!hasEthernetIpv4)
        {
            return GetString("Ethernet.WaitingAddress");
        }

        return isDhcpEnabled
            ? GetString("Ethernet.DhcpEnabled")
            : GetString("Ethernet.StaticConfiguration");
    }

    private string GetString(string key)
    {
        return _localizationService.Strings[key];
    }
}
