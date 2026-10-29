// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Models;
using Foundry.Connect.Models.Network;
using Foundry.Connect.Network;
using Foundry.Wpf.Localization;

namespace Foundry.Connect.ViewModels;

/// <summary>Temporary bridge from typed network state to the two lot-1 views; deleted in lot 3.</summary>
/// <remarks>
/// It only reshapes the published <see cref="NetworkState"/> and maps typed failure codes to existing resource keys
/// (L2-3): it never reads status text and never decides anything about connections.
/// </remarks>
internal sealed class LegacyNetworkStateAdapter(ILocalizationService localization)
{
    private readonly ILocalizationService _localization = localization ?? throw new ArgumentNullException(nameof(localization));

    /// <summary>
    /// Builds the snapshot the lot-1 views bind to. A captive portal is not internet access (D2) and an APIPA address
    /// is not an IPv4 configuration (L2-15).
    /// </summary>
    /// <param name="state">Published network state.</param>
    /// <returns>The legacy snapshot.</returns>
    public NetworkStatusSnapshot ToSnapshot(NetworkState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        WifiAvailability availability = state.Wifi.Availability;
        bool isWifiRuntimeAvailable = availability is WifiAvailability.Available or WifiAvailability.NoAdapter;
        EthernetInterfaceState? primary = state.Ethernet.Primary;
        bool hasEthernetAdapter = state.Ethernet.HasAdapter;
        bool isEthernetConnected = state.Ethernet.HasLink;
        bool hasEthernetIpv4 = primary?.HasUsableAddress == true;
        bool hasDhcpLease = primary?.IpConfiguration == IpConfigurationKind.Dhcp;
        WifiConnection? connection = state.Wifi.Connection;
        string unavailable = GetString("Common.Unavailable");

        return new NetworkStatusSnapshot
        {
            LayoutMode = isWifiRuntimeAvailable ? NetworkLayoutMode.EthernetWifi : NetworkLayoutMode.EthernetOnly,
            HasInternetAccess = state.Internet == InternetStatus.Online,
            HasEthernetAdapter = hasEthernetAdapter,
            IsEthernetConnected = isEthernetConnected,
            HasDhcpLease = hasDhcpLease,
            HasEthernetIpv4 = hasEthernetIpv4,
            IsWifiRuntimeAvailable = isWifiRuntimeAvailable,
            HasWirelessAdapter = availability == WifiAvailability.Available,
            EthernetStatusText = BuildEthernetStatusText(hasEthernetAdapter, isEthernetConnected, hasEthernetIpv4),
            EthernetSecondaryStatusText = BuildEthernetSecondaryStatusText(hasEthernetAdapter, isEthernetConnected, hasEthernetIpv4, hasDhcpLease),
            EthernetAdapterName = primary?.Name ?? unavailable,
            EthernetIpAddress = primary?.Ipv4Address ?? unavailable,
            EthernetGateway = primary?.Gateway ?? unavailable,
            ConnectedWifiSsid = connection?.Ssid.DisplayName,
            ConnectedWifiSsidHex = connection?.Ssid.Hex,
            IsConnectedWifiProvisioned = connection?.Source == WifiConnectionSource.Provisioned,
            WifiNetworks = state.Wifi.Networks
                .Select(static network => new WifiNetworkSummary
                {
                    Ssid = network.Ssid.DisplayName,
                    SsidHex = network.Ssid.Hex,
                    SignalStrengthPercent = network.SignalQuality,
                    Authentication = LegacyAuthenticationLabel(network.Security),
                    Encryption = string.Empty
                })
                .ToArray()
        };
    }

    /// <summary>
    /// Maps an operation outcome to the resource key of the feedback shown under the panel that started it.
    /// </summary>
    /// <param name="outcome">Operation outcome.</param>
    /// <param name="fromProvisionedPanel">Whether the provisioned panel started the operation (only a disconnect differs).</param>
    /// <returns>The resource key, or <see langword="null"/> when nothing is shown (success, no change, cancellation).</returns>
    public static string? ResolveFeedbackKey(OperationOutcome outcome, bool fromProvisionedPanel)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (outcome.Status != OperationStatus.Failed)
        {
            return null;
        }

        if (outcome.Failure is NetworkFailureCode.NoWirelessAdapter or NetworkFailureCode.WifiRuntimeMissing)
        {
            return "Wifi.NoAdapterAvailable";
        }

        return outcome.Operation switch
        {
            NetworkOperationKind.ConnectWifiNetwork => outcome.Failure == NetworkFailureCode.UnsupportedSecurity
                ? "Wifi.ActionSelectedNotSupported"
                : "Wifi.ActionConnectFailed",
            NetworkOperationKind.ConnectProvisionedWifi => outcome.Failure is NetworkFailureCode.WifiFeatureDisabled
                or NetworkFailureCode.ProvisionedProfileUnavailable
                or NetworkFailureCode.WifiSecretUnavailable
                ? "Wifi.ActionProvisionedProfileUnavailable"
                : "Wifi.ActionProvisionedConnectFailed",
            NetworkOperationKind.DisconnectWifi => fromProvisionedPanel
                ? "Wifi.ActionProvisionedDisconnectFailed"
                : "Wifi.ActionDisconnectFailed",

            // Provisioning feedback comes from ResolveProvisioningFeedbackKey.
            _ => null
        };
    }

    /// <summary>
    /// Maps the startup provisioning outcome to the provisioned panel feedback. Only Wi-Fi problems are shown there; a
    /// Wi-Fi part that throws ends <see cref="ProvisionedWifiStatus.Unavailable"/>, so with
    /// <see cref="ProvisionedWifiStatus.Ready"/> an <see cref="NetworkFailureCode.Unexpected"/> code comes from the
    /// wired part.
    /// </summary>
    /// <param name="outcome">Provisioning outcome.</param>
    /// <returns>The resource key, or <see langword="null"/> when nothing is shown.</returns>
    public static string? ResolveProvisioningFeedbackKey(ProvisioningOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return outcome.Wifi.Status switch
        {
            ProvisionedWifiStatus.Unavailable => "Wifi.ActionProvisionedProfileUnavailable",
            ProvisionedWifiStatus.Ready when outcome.Failures.Any(static failure =>
                failure is NetworkFailureCode.WifiCertificateImportFailed or NetworkFailureCode.WifiProfileRejected) => "Wifi.ActionApplyProvisionedFailed",
            _ => null
        };
    }

    /// <summary>
    /// Returns the authentication label the native discovery produced before lot 2, which the lot-1 view model still
    /// translates and classifies.
    /// </summary>
    /// <param name="security">Typed security.</param>
    /// <returns>The legacy label.</returns>
    public static string LegacyAuthenticationLabel(WifiSecurityType security)
    {
        return security switch
        {
            WifiSecurityType.Open => "Open",
            WifiSecurityType.Owe => "OWE",
            WifiSecurityType.Wep => "WEP",
            WifiSecurityType.WpaPersonal => "WPA-Personal",
            WifiSecurityType.Wpa2Personal => "WPA2-Personal",
            WifiSecurityType.Wpa3Personal => "WPA3-Personal",
            WifiSecurityType.WpaEnterprise => "WPA-Enterprise",
            WifiSecurityType.Wpa2Enterprise => "WPA2-Enterprise",
            WifiSecurityType.Wpa3Enterprise => "WPA3-Enterprise",
            _ => "Vendor-specific"
        };
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
        return _localization.Strings[key];
    }
}
