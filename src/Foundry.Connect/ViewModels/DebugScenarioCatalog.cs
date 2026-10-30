// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;

namespace Foundry.Connect.ViewModels;

/// <summary>
/// Simulated network situations for Debug › Scenarios; <see cref="Live"/> means the real network.
/// </summary>
public enum ConnectDebugScenario
{
    /// <summary>The real network: no simulation.</summary>
    Live,

    /// <summary>S0: the first observation has not arrived.</summary>
    Checking,

    /// <summary>S1: no Ethernet adapter and no Wi-Fi.</summary>
    NoAdapter,

    /// <summary>S2: an Ethernet adapter without a cable.</summary>
    NoLink,

    /// <summary>S3: a cable is plugged but no address was assigned yet.</summary>
    WaitingForAddress,

    /// <summary>S4: only a link-local address, which never reaches the internet.</summary>
    ApipaAddress,

    /// <summary>S5: a DHCP address but no internet.</summary>
    EthernetNoInternet,

    /// <summary>S6: a DHCP address behind a captive portal.</summary>
    CaptivePortal,

    /// <summary>S7: Ethernet with internet.</summary>
    EthernetReady,

    /// <summary>S8: wired 802.1X authentication failed.</summary>
    Dot1xFailed,

    /// <summary>S9: the WLAN runtime is missing although Wi-Fi is provisioned.</summary>
    WifiRuntimeMissing,

    /// <summary>S10: the WLAN runtime is present but there is no wireless adapter.</summary>
    NoWifiAdapter,

    /// <summary>S11: Wi-Fi networks in range, no provisioned profile.</summary>
    WifiNetworks,

    /// <summary>S12: Wi-Fi networks in range, the provisioned profile is unusable.</summary>
    ProvisionedWifiUnavailable,

    /// <summary>S13: connected to the provisioned Wi-Fi with internet.</summary>
    WifiReady,

    /// <summary>S14: the provisioned Wi-Fi stays locked after the deployment password prompt was cancelled.</summary>
    ProvisionedWifiLocked,

    /// <summary>S15: a cable is plugged and wired 802.1X is still authenticating.</summary>
    Dot1xAuthenticating,

    /// <summary>S16: a provisioned Wi-Fi connection is in progress.</summary>
    WifiConnecting,

    /// <summary>S17: no Ethernet adapter; Wi-Fi networks in range without the provisioned SSID, the profile is ready.</summary>
    WifiOnly
}

/// <summary>
/// Builds the 18 simulated <see cref="NetworkState"/> values behind Debug › Scenarios.
/// </summary>
/// <remarks>
/// The states are fixed fake data: the SSIDs are sample names and the addresses come from the documentation range
/// (TEST-NET-1), so a simulated state is safe in screenshots. They are never logged (the Logs stream mirrors every log
/// property). The catalog only creates states; the caller enforces debug safety and makes sure a simulated state never
/// reaches the real network.
/// </remarks>
public static class DebugScenarioCatalog
{
    private static readonly Guid WifiAdapterId = Guid.Parse("0f0f0f0f-0000-4000-8000-000000000001");

    private static readonly WifiSsid ProvisionedSsid = WifiSsid.FromText("Foundry Lab");

    /// <summary>
    /// Gets every scenario in declaration order, <see cref="ConnectDebugScenario.Live"/> first.
    /// </summary>
    public static IReadOnlyList<ConnectDebugScenario> All { get; } = Array.AsReadOnly(Enum.GetValues<ConnectDebugScenario>());

    /// <summary>
    /// Gets the resource key of a scenario's menu text.
    /// </summary>
    /// <param name="scenario">The scenario.</param>
    /// <returns>The resource key, <c>Debug.Scenario.&lt;name&gt;</c>.</returns>
    public static string ResourceKey(ConnectDebugScenario scenario) => $"Debug.Scenario.{scenario}";

    /// <summary>
    /// Creates the simulated state of a scenario.
    /// </summary>
    /// <param name="scenario">A simulated scenario.</param>
    /// <param name="now">The observation time of the state.</param>
    /// <returns>An automatic-mode state without outcome; only <see cref="ConnectDebugScenario.WifiConnecting"/> carries a running operation.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scenario"/> is <see cref="ConnectDebugScenario.Live"/> or undefined.</exception>
    public static NetworkState Create(ConnectDebugScenario scenario, DateTimeOffset now)
    {
        NetworkState state = NetworkState.Initial(now);

        return scenario switch
        {
            ConnectDebugScenario.Checking => state,
            ConnectDebugScenario.NoAdapter => state with { Internet = InternetStatus.NoInternet },
            ConnectDebugScenario.NoLink => WithEthernet(state, Interface(hasLink: false, IpConfigurationKind.None), InternetStatus.NoInternet),
            ConnectDebugScenario.WaitingForAddress => WithEthernet(state, Interface(hasLink: true, IpConfigurationKind.None), InternetStatus.NoInternet),
            ConnectDebugScenario.ApipaAddress => WithEthernet(state, Interface(hasLink: true, IpConfigurationKind.Apipa, "169.254.10.20"), InternetStatus.NoInternet),
            ConnectDebugScenario.EthernetNoInternet => WithEthernet(state, DhcpInterface(), InternetStatus.NoInternet),
            ConnectDebugScenario.CaptivePortal => WithEthernet(state, DhcpInterface(), InternetStatus.CaptivePortal),
            ConnectDebugScenario.EthernetReady => WithEthernet(state, DhcpInterface(), InternetStatus.Online),
            ConnectDebugScenario.Dot1xFailed => state with
            {
                Ethernet = new EthernetState(
                    [Interface(hasLink: true, IpConfigurationKind.None)],
                    new Dot1xState(Dot1xAuthenticationStatus.Failed, Dot1xFailureReason.AuthenticationFailed)),
                Internet = InternetStatus.NoInternet
            },
            ConnectDebugScenario.WifiRuntimeMissing => WithWifi(
                state,
                new WifiState(WifiAvailability.RuntimeMissing, null, [], new ProvisionedWifiState(ProvisionedWifiStatus.Ready, Ssid: ProvisionedSsid)),
                InternetStatus.NoInternet),
            ConnectDebugScenario.NoWifiAdapter => WithWifi(
                state,
                new WifiState(WifiAvailability.NoAdapter, null, [], ProvisionedWifiState.NotConfigured),
                InternetStatus.NoInternet),
            ConnectDebugScenario.WifiNetworks => WithWifi(
                state,
                new WifiState(WifiAvailability.Available, null, VisibleNetworks(), ProvisionedWifiState.NotConfigured),
                InternetStatus.NoInternet),
            ConnectDebugScenario.ProvisionedWifiUnavailable => WithWifi(
                state,
                new WifiState(
                    WifiAvailability.Available,
                    null,
                    VisibleNetworks(),
                    new ProvisionedWifiState(ProvisionedWifiStatus.Unavailable, ProvisionedWifiUnavailableReason.SecretUnavailable, ProvisionedSsid)),
                InternetStatus.NoInternet),
            ConnectDebugScenario.WifiReady => WithWifi(
                state,
                new WifiState(
                    WifiAvailability.Available,
                    new WifiConnection(WifiAdapterId, ProvisionedSsid, "Foundry Lab", WifiSecurityType.Wpa3Personal, WifiConnectionSource.Provisioned),
                    VisibleNetworks(),
                    new ProvisionedWifiState(ProvisionedWifiStatus.Ready, Ssid: ProvisionedSsid, ProfileName: "Foundry Lab")),
                InternetStatus.Online),
            ConnectDebugScenario.ProvisionedWifiLocked => WithWifi(
                state,
                new WifiState(
                    WifiAvailability.Available,
                    null,
                    VisibleNetworks(),
                    new ProvisionedWifiState(ProvisionedWifiStatus.Locked, ProvisionedWifiUnavailableReason.UnlockCancelled, ProvisionedSsid)),
                InternetStatus.NoInternet),
            ConnectDebugScenario.Dot1xAuthenticating => state with
            {
                Ethernet = new EthernetState(
                    [Interface(hasLink: true, IpConfigurationKind.None)],
                    new Dot1xState(Dot1xAuthenticationStatus.Authenticating, Dot1xFailureReason.None)),
                Internet = InternetStatus.NoInternet
            },
            ConnectDebugScenario.WifiConnecting => WithWifi(
                state,
                new WifiState(
                    WifiAvailability.Available,
                    null,
                    VisibleNetworks(),
                    new ProvisionedWifiState(ProvisionedWifiStatus.Ready, Ssid: ProvisionedSsid, ProfileName: "Foundry Lab")),
                InternetStatus.NoInternet) with { ActiveOperation = NetworkOperationKind.ConnectProvisionedWifi },
            ConnectDebugScenario.WifiOnly => state with
            {
                Wifi = new WifiState(
                    WifiAvailability.Available,
                    null,
                    VisibleNetworks(includeProvisioned: false),
                    new ProvisionedWifiState(ProvisionedWifiStatus.Ready, Ssid: ProvisionedSsid, ProfileName: "Foundry Lab")),
                Internet = InternetStatus.NoInternet
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Only the simulated scenarios can be created; Live is the real network."),
        };
    }

    private static NetworkState WithEthernet(NetworkState state, EthernetInterfaceState networkInterface, InternetStatus internet) =>
        state with { Ethernet = new EthernetState([networkInterface], Dot1xState.NotConfigured), Internet = internet };

    // The Wi-Fi scenarios keep an unplugged Ethernet adapter, the usual WinPE laptop situation.
    private static NetworkState WithWifi(NetworkState state, WifiState wifi, InternetStatus internet) =>
        WithEthernet(state, Interface(hasLink: false, IpConfigurationKind.None), internet) with { Wifi = wifi };

    private static EthernetInterfaceState DhcpInterface() => Interface(hasLink: true, IpConfigurationKind.Dhcp, "192.0.2.10", "192.0.2.1");

    private static EthernetInterfaceState Interface(bool hasLink, IpConfigurationKind configuration, string? address = null, string? gateway = null) =>
        new("debug-ethernet", "Ethernet", hasLink, configuration, address, gateway);

    // includeProvisioned: false drops the provisioned SSID, so the provisioned profile is "not in range".
    private static VisibleWifiNetwork[] VisibleNetworks(bool includeProvisioned = true) =>
    [
        new(WifiAdapterId, WifiSsid.FromText("Guest"), WifiSecurityType.Open, 82),
        new(WifiAdapterId, WifiSsid.FromText("Workshop"), WifiSecurityType.Wpa2Personal, 64),
        .. includeProvisioned ? [new VisibleWifiNetwork(WifiAdapterId, ProvisionedSsid, WifiSecurityType.Wpa3Personal, 48)] : Array.Empty<VisibleWifiNetwork>(),
        new(WifiAdapterId, WifiSsid.FromText("Corp-Secure"), WifiSecurityType.Wpa2Enterprise, 30)
    ];
}
