using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Foundry.Connect.Models;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Models.Network;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Services.Network;

public sealed partial class NetworkStatusService : INetworkStatusService
{
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true
    });

    private readonly FoundryConnectConfiguration _configuration;
    private readonly ILogger<NetworkStatusService> _logger;

    public NetworkStatusService(
        FoundryConnectConfiguration configuration,
        ILogger<NetworkStatusService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<NetworkStatusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
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
        bool isWifiRuntimeAvailable = _configuration.Capabilities.WifiProvisioned && await IsWifiRuntimeAvailableAsync(cancellationToken).ConfigureAwait(false);
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
            WifiStatusText = BuildWifiStatusText(isWifiRuntimeAvailable, hasWirelessAdapter, wifiNetworks.Count),
            AdapterName = adapterName,
            IpAddress = ipAddress,
            SubnetMask = subnetMask,
            GatewayAddress = gateway,
            DnsServers = dnsServers,
            DhcpText = hasDhcpLease ? "DHCP lease detected." : "No DHCP lease detected.",
            ConnectionSummary = BuildConnectionSummary(isEthernetConnected, hasInternetAccess, isWifiRuntimeAvailable, wifiNetworks.Count),
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

    private async Task<bool> IsWifiRuntimeAvailableAsync(CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await ExecuteProcessAsync("netsh", "wlan show interfaces", cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return false;
        }

        string output = $"{result.StandardOutput}\n{result.StandardError}";
        return !output.Contains("wlansvc", StringComparison.OrdinalIgnoreCase) &&
               !output.Contains("wireless autoconfig service", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyList<WifiNetworkSummary>> DiscoverWifiNetworksAsync(CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await ExecuteProcessAsync("netsh", "wlan show networks mode=bssid", cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return Array.Empty<WifiNetworkSummary>();
        }

        return ParseWifiNetworks(result.StandardOutput);
    }

    private static IReadOnlyList<WifiNetworkSummary> ParseWifiNetworks(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<WifiNetworkSummary>();
        }

        Dictionary<string, WifiNetworkSummary> networks = new(StringComparer.OrdinalIgnoreCase);
        string? currentSsid = null;
        string authentication = "Unknown";
        string encryption = "Unknown";
        int signal = 0;

        foreach (string rawLine in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            Match ssidMatch = SsidRegex().Match(line);
            if (ssidMatch.Success)
            {
                CommitNetwork(networks, currentSsid, signal, authentication, encryption);
                currentSsid = NormalizeSsid(ssidMatch.Groups["ssid"].Value);
                authentication = "Unknown";
                encryption = "Unknown";
                signal = 0;
                continue;
            }

            if (currentSsid is null)
            {
                continue;
            }

            if (TryReadValue(line, "authentication", out string authValue))
            {
                authentication = authValue;
                continue;
            }

            if (TryReadValue(line, "encryption", out string encryptionValue))
            {
                encryption = encryptionValue;
                continue;
            }

            Match signalMatch = SignalRegex().Match(line);
            if (signalMatch.Success &&
                int.TryParse(signalMatch.Groups["value"].Value, out int parsedSignal))
            {
                signal = Math.Max(signal, parsedSignal);
            }
        }

        CommitNetwork(networks, currentSsid, signal, authentication, encryption);

        return networks.Values
            .OrderByDescending(static network => network.SignalStrengthPercent)
            .ThenBy(static network => network.Ssid, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void CommitNetwork(
        IDictionary<string, WifiNetworkSummary> networks,
        string? ssid,
        int signal,
        string authentication,
        string encryption)
    {
        if (string.IsNullOrWhiteSpace(ssid))
        {
            return;
        }

        if (networks.TryGetValue(ssid, out WifiNetworkSummary? existing) &&
            existing.SignalStrengthPercent >= signal)
        {
            return;
        }

        networks[ssid] = new WifiNetworkSummary
        {
            Ssid = ssid,
            SignalStrengthPercent = Math.Clamp(signal, 0, 100),
            Authentication = authentication,
            Encryption = encryption
        };
    }

    private static string NormalizeSsid(string value)
    {
        string normalized = value.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "Hidden network" : normalized;
    }

    private static bool TryReadValue(string line, string key, out string value)
    {
        int separatorIndex = line.IndexOf(':');
        if (separatorIndex <= 0)
        {
            value = string.Empty;
            return false;
        }

        string left = line[..separatorIndex].Trim();
        if (!left.Contains(key, StringComparison.OrdinalIgnoreCase))
        {
            value = string.Empty;
            return false;
        }

        value = line[(separatorIndex + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(value);
    }

    private async Task<ProcessExecutionResult> ExecuteProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return new ProcessExecutionResult(
                process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Process execution failed for {FileName} {Arguments}.", fileName, arguments);
            return new ProcessExecutionResult(-1, string.Empty, ex.Message);
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

    private static string BuildWifiStatusText(bool isWifiRuntimeAvailable, bool hasWirelessAdapter, int networkCount)
    {
        if (!isWifiRuntimeAvailable)
        {
            return "Wi-Fi UI is disabled because Wi-Fi support is not available at runtime.";
        }

        if (!hasWirelessAdapter)
        {
            return "Wi-Fi support is provisioned, but no wireless adapter is currently detected.";
        }

        if (networkCount == 0)
        {
            return "Wireless adapter detected, but no Wi-Fi networks were discovered.";
        }

        return $"{networkCount} Wi-Fi network(s) discovered.";
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

    [GeneratedRegex(@"^SSID\s+\d+\s*:\s*(?<ssid>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SsidRegex();

    [GeneratedRegex(@"(?<value>\d{1,3})\s*%", RegexOptions.CultureInvariant)]
    private static partial Regex SignalRegex();

    private readonly record struct ProcessExecutionResult(int ExitCode, string StandardOutput, string StandardError);
}
