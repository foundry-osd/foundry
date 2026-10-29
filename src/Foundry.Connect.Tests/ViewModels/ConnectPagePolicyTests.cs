// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.ViewModels;

namespace Foundry.Connect.Tests.ViewModels;

public sealed class ConnectPagePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);
    private static readonly EthernetInterfaceState NoLink = new("eth0", "Ethernet", false, IpConfigurationKind.None, null, null);
    private static readonly EthernetInterfaceState LinkNoAddress = new("eth0", "Ethernet", true, IpConfigurationKind.None, null, null);
    private static readonly EthernetInterfaceState Apipa = new("eth0", "Ethernet", true, IpConfigurationKind.Apipa, "169.254.1.2", null);
    private static readonly EthernetInterfaceState Dhcp = new("eth0", "Ethernet", true, IpConfigurationKind.Dhcp, "192.0.2.10", "192.0.2.1");

    private static NetworkState State(
        InternetStatus internet,
        EthernetInterfaceState? ethernet = null,
        WifiAvailability wifi = WifiAvailability.Disabled,
        bool wifiConnected = false,
        NetworkOperationKind? operation = null) =>
        NetworkState.Initial(Now) with
        {
            Internet = internet,
            Ethernet = new EthernetState(ethernet is null ? [] : [ethernet], Dot1xState.NotConfigured),
            Wifi = WifiState.Initial with
            {
                Availability = wifi,
                Connection = wifiConnected ? new WifiConnection(Guid.Empty, WifiSsid.FromText("Lab"), "Lab", WifiSecurityType.Wpa2Personal) : null
            },
            ActiveOperation = operation
        };

    public static TheoryData<string, NetworkState, ConnectPageStatus, BlockedReason> Rows => new()
    {
        { "startup", State(InternetStatus.Checking), ConnectPageStatus.Checking, BlockedReason.None },
        { "applying", State(InternetStatus.Checking, operation: NetworkOperationKind.ApplyProvisioning), ConnectPageStatus.Waiting, BlockedReason.None },
        { "connecting_offline", State(InternetStatus.NoInternet, NoLink, WifiAvailability.Available, operation: NetworkOperationKind.ConnectWifiNetwork), ConnectPageStatus.Waiting, BlockedReason.None },
        { "online_while_connecting", State(InternetStatus.Online, Dhcp, operation: NetworkOperationKind.ConnectWifiNetwork), ConnectPageStatus.Ready, BlockedReason.None },
        { "online", State(InternetStatus.Online, Dhcp), ConnectPageStatus.Ready, BlockedReason.None },
        { "no_adapter", State(InternetStatus.NoInternet), ConnectPageStatus.Blocked, BlockedReason.NoAdapter },
        { "wifi_runtime_missing_only", State(InternetStatus.NoInternet, wifi: WifiAvailability.RuntimeMissing), ConnectPageStatus.Blocked, BlockedReason.NoAdapter },
        { "no_link", State(InternetStatus.NoInternet, NoLink), ConnectPageStatus.Blocked, BlockedReason.NoLink },
        { "wifi_available_not_joined", State(InternetStatus.NoInternet, wifi: WifiAvailability.Available), ConnectPageStatus.Blocked, BlockedReason.NoLink },
        { "waiting_for_address", State(InternetStatus.NoInternet, LinkNoAddress), ConnectPageStatus.Blocked, BlockedReason.NoIpAddress },
        { "apipa", State(InternetStatus.NoInternet, Apipa), ConnectPageStatus.Blocked, BlockedReason.NoIpAddress },
        { "ethernet_no_internet", State(InternetStatus.NoInternet, Dhcp), ConnectPageStatus.Blocked, BlockedReason.NoInternet },
        { "wifi_no_internet", State(InternetStatus.NoInternet, NoLink, WifiAvailability.Available, wifiConnected: true), ConnectPageStatus.Blocked, BlockedReason.NoInternet },
        { "captive_portal", State(InternetStatus.CaptivePortal, Dhcp), ConnectPageStatus.Blocked, BlockedReason.CaptivePortal }
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void StatusAndReason_ScenarioTable(string scenario, NetworkState state, ConnectPageStatus status, BlockedReason reason)
    {
        Assert.True(status == ConnectPagePolicy.ResolveStatus(state), scenario);
        Assert.True(reason == ConnectPagePolicy.ResolveBlockedReason(state), scenario);
    }

    [Theory]
    [InlineData(ConnectPageStatus.Ready, true, false, false, ConnectFocusTarget.Continue)]
    [InlineData(ConnectPageStatus.Blocked, false, true, true, ConnectFocusTarget.ProvisionedConnect)]
    [InlineData(ConnectPageStatus.Blocked, false, false, true, ConnectFocusTarget.WifiNetworks)]
    [InlineData(ConnectPageStatus.Blocked, false, false, false, ConnectFocusTarget.Retry)]
    [InlineData(ConnectPageStatus.Blocked, true, true, true, ConnectFocusTarget.None)]
    [InlineData(ConnectPageStatus.Checking, false, true, true, ConnectFocusTarget.None)]
    [InlineData(ConnectPageStatus.Waiting, false, true, true, ConnectFocusTarget.None)]
    public void ResolveFocus_ScenarioTable(ConnectPageStatus status, bool interacted, bool provisioned, bool networks, ConnectFocusTarget expected)
    {
        Assert.Equal(expected, ConnectPagePolicy.ResolveFocus(status, interacted, provisioned, networks));
    }
}
