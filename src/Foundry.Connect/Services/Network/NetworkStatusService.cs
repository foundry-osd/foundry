// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.Http;
using System.Net.NetworkInformation;
using System.Diagnostics;
using Foundry.Connect.Models;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Services.Localization;
using Foundry.Connect.Models.Network;
using Foundry.Utilities.Networking;
using Foundry.Utilities.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Services.Network;

/// <summary>
/// Builds network status snapshots from Windows adapter state, native WLAN APIs, and configured Internet probes.
/// </summary>
public sealed class NetworkStatusService : INetworkStatusService
{
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true
    });
    private static readonly TimeSpan WifiDiscoveryGracePeriod = TimeSpan.FromSeconds(15);

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
        INetworkAdapterSnapshotProvider? networkAdapterSnapshotProvider = null)
    {
        _configuration = configuration;
        _localizationService = localizationService;
        _logger = logger;
        _networkAdapterSnapshotProvider = networkAdapterSnapshotProvider ?? new WindowsNetworkAdapterSnapshotProvider();
    }

    public async Task<NetworkStatusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
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
        string? connectedWifiSsid = isWifiRuntimeAvailable ? NativeWifiApi.GetConnectedSsid() : null;
        IReadOnlyList<WifiNetworkSummary> wifiNetworks = isWifiRuntimeAvailable
            ? await DiscoverWifiNetworksAsync(cancellationToken).ConfigureAwait(false)
            : Array.Empty<WifiNetworkSummary>();
        bool hasDhcpLease = connectedEthernetAdapter?.IsDhcpEnabled == true;
        bool hasInternetAccess = await ProbeInternetAsync(cancellationToken).ConfigureAwait(false);

        NetworkIpv4AddressSnapshot? ethernetIpv4Information = ethernetDisplayAdapter?.Ipv4Addresses.FirstOrDefault();
        string? ethernetGateway = connectedEthernetAdapter?.Gateways.FirstOrDefault();
        bool hasEthernetIpv4 = ethernetIpv4Information is not null;

        return new NetworkStatusSnapshot
        {
            LayoutMode = isWifiRuntimeAvailable ? NetworkLayoutMode.EthernetWifi : NetworkLayoutMode.EthernetOnly,
            HasInternetAccess = hasInternetAccess,
            HasEthernetAdapter = hasEthernetAdapter,
            IsEthernetConnected = isEthernetConnected,
            HasDhcpLease = hasDhcpLease,
            HasEthernetIpv4 = hasEthernetIpv4,
            IsWifiRuntimeAvailable = isWifiRuntimeAvailable,
            HasWirelessAdapter = hasWirelessAdapter,
            EthernetStatusText = BuildEthernetStatusText(hasEthernetAdapter, isEthernetConnected, hasEthernetIpv4),
            EthernetSecondaryStatusText = BuildEthernetSecondaryStatusText(hasEthernetAdapter, isEthernetConnected, hasEthernetIpv4, hasDhcpLease),
            EthernetAdapterName = ethernetDisplayAdapter?.Name ?? GetString("Common.Unavailable"),
            EthernetIpAddress = ethernetIpv4Information?.Address ?? GetString("Common.Unavailable"),
            EthernetGateway = ethernetGateway ?? GetString("Common.Unavailable"),
            ConnectedWifiSsid = connectedWifiSsid,
            WifiNetworks = wifiNetworks
        };
    }

    private async Task<bool> ProbeInternetAsync(CancellationToken cancellationToken)
    {
        foreach (string probeUri in _configuration.InternetProbe.ProbeUris)
        {
            string safeProbeUri = Uri.TryCreate(probeUri, UriKind.Absolute, out Uri? parsedProbeUri)
                ? LogValueSanitizer.SanitizeUri(parsedProbeUri)
                : "<invalid-uri>";
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
                _logger.LogDebug("Internet probe timed out. ProbeUri={ProbeUri}", safeProbeUri);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    "Internet probe failed. ProbeUri={ProbeUri}, FailureType={FailureType}",
                    safeProbeUri,
                    ex.GetType().Name);
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

    private string BuildEthernetSecondaryStatusText(bool hasEthernetAdapter, bool isEthernetConnected, bool hasEthernetIpv4, bool hasDhcpLease)
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
            return GetString("Ethernet.WaitingDhcp");
        }

        return hasDhcpLease
            ? GetString("Ethernet.DhcpLeaseDetected")
            : GetString("Ethernet.StaticConfigurationDetected");
    }

    private string GetString(string key)
    {
        return _localizationService.Strings[key];
    }
}
