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
        NetworkOperationKind? operation = null,
        Dot1xAuthenticationStatus dot1x = Dot1xAuthenticationStatus.NotConfigured) =>
        NetworkState.Initial(Now) with
        {
            Internet = internet,
            Ethernet = new EthernetState(
                ethernet is null ? [] : [ethernet],
                dot1x == Dot1xAuthenticationStatus.NotConfigured
                    ? Dot1xState.NotConfigured
                    : new Dot1xState(dot1x, dot1x switch
                    {
                        Dot1xAuthenticationStatus.Failed => Dot1xFailureReason.AuthenticationFailed,
                        Dot1xAuthenticationStatus.Unavailable => Dot1xFailureReason.SecretUnavailable,
                        _ => Dot1xFailureReason.None
                    })),
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
        { "captive_portal", State(InternetStatus.CaptivePortal, Dhcp), ConnectPageStatus.Blocked, BlockedReason.CaptivePortal },
        { "dot1x_authenticating_without_address", State(InternetStatus.NoInternet, LinkNoAddress, dot1x: Dot1xAuthenticationStatus.Authenticating), ConnectPageStatus.Waiting, BlockedReason.None },
        { "dot1x_authenticating_apipa", State(InternetStatus.NoInternet, Apipa, dot1x: Dot1xAuthenticationStatus.Authenticating), ConnectPageStatus.Waiting, BlockedReason.None },
        { "dot1x_authenticating_with_address", State(InternetStatus.NoInternet, Dhcp, dot1x: Dot1xAuthenticationStatus.Authenticating), ConnectPageStatus.Blocked, BlockedReason.NoInternet },
        { "dot1x_authenticating_on_wifi", State(InternetStatus.NoInternet, LinkNoAddress, WifiAvailability.Available, wifiConnected: true, dot1x: Dot1xAuthenticationStatus.Authenticating), ConnectPageStatus.Blocked, BlockedReason.NoInternet },
        { "dot1x_authenticating_without_link", State(InternetStatus.NoInternet, NoLink, dot1x: Dot1xAuthenticationStatus.Authenticating), ConnectPageStatus.Blocked, BlockedReason.NoLink },
        { "dot1x_authenticating_online", State(InternetStatus.Online, LinkNoAddress, WifiAvailability.Available, wifiConnected: true, dot1x: Dot1xAuthenticationStatus.Authenticating), ConnectPageStatus.Ready, BlockedReason.None },
        { "dot1x_failed_without_address", State(InternetStatus.NoInternet, LinkNoAddress, dot1x: Dot1xAuthenticationStatus.Failed), ConnectPageStatus.Blocked, BlockedReason.WiredAuthentication },
        { "dot1x_unavailable_without_address", State(InternetStatus.NoInternet, LinkNoAddress, dot1x: Dot1xAuthenticationStatus.Unavailable), ConnectPageStatus.Blocked, BlockedReason.WiredAuthentication },
        { "dot1x_failed_apipa", State(InternetStatus.NoInternet, Apipa, dot1x: Dot1xAuthenticationStatus.Failed), ConnectPageStatus.Blocked, BlockedReason.WiredAuthentication },
        { "dot1x_failed_with_address", State(InternetStatus.NoInternet, Dhcp, dot1x: Dot1xAuthenticationStatus.Failed), ConnectPageStatus.Blocked, BlockedReason.NoInternet },
        { "dot1x_failed_on_wifi", State(InternetStatus.NoInternet, LinkNoAddress, WifiAvailability.Available, wifiConnected: true, dot1x: Dot1xAuthenticationStatus.Failed), ConnectPageStatus.Blocked, BlockedReason.NoInternet },
        { "dot1x_failed_without_link", State(InternetStatus.NoInternet, NoLink, dot1x: Dot1xAuthenticationStatus.Failed), ConnectPageStatus.Blocked, BlockedReason.NoLink },
        { "dot1x_profile_applied_without_address", State(InternetStatus.NoInternet, LinkNoAddress, dot1x: Dot1xAuthenticationStatus.ProfileApplied), ConnectPageStatus.Blocked, BlockedReason.NoIpAddress },
        { "dot1x_failed_captive_portal", State(InternetStatus.CaptivePortal, Dhcp, dot1x: Dot1xAuthenticationStatus.Failed), ConnectPageStatus.Blocked, BlockedReason.CaptivePortal }
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void StatusAndReason_ScenarioTable(string scenario, NetworkState state, ConnectPageStatus status, BlockedReason reason)
    {
        Assert.True(status == ConnectPagePolicy.ResolveStatus(state), scenario);
        Assert.True(reason == ConnectPagePolicy.ResolveBlockedReason(state), scenario);
    }

    [Theory]
    [InlineData(WifiAvailability.Disabled, false)]
    [InlineData(WifiAvailability.RuntimeMissing, false)]
    [InlineData(WifiAvailability.NoAdapter, true)]
    [InlineData(WifiAvailability.Available, true)]
    public void IsWifiOffered_OnlyWhenTheWlanStackRuns(WifiAvailability availability, bool expected)
    {
        Assert.Equal(expected, ConnectPagePolicy.IsWifiOffered(availability));
    }

    [Theory]
    [InlineData(true, WifiAvailability.RuntimeMissing, true)]
    [InlineData(true, WifiAvailability.NoAdapter, true)]
    [InlineData(false, WifiAvailability.Available, true)]
    [InlineData(false, WifiAvailability.NoAdapter, false)]
    [InlineData(false, WifiAvailability.Disabled, false)]
    public void IsWifiSectionShown_FollowsTheSessionLayoutThenAWorkingWifi(bool usesTwoColumns, WifiAvailability availability, bool expected)
    {
        Assert.Equal(expected, ConnectPagePolicy.IsWifiSectionShown(usesTwoColumns, availability));
    }

    [Theory]
    [InlineData(ConnectPageStatus.Ready, false, true, true, ConnectFocusTarget.Continue)]
    [InlineData(ConnectPageStatus.Ready, true, false, false, ConnectFocusTarget.None)]
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
