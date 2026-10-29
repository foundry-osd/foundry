// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;
using Foundry.Core.Models.Configuration;

namespace Foundry.Connect.Tests.Network;

public sealed class RoamingSelectionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid WifiInterface = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid UsbWifiInterface = Guid.Parse("dddddddd-0000-0000-0000-000000000002");

    public static TheoryData<string, string[]> WifiScenarios => new()
    {
        // scenario, expected profile names in roaming order
        { "ethernet_active_one_joined", ["Lab"] },
        { "wifi_path_one_joined", ["Lab"] },
        { "two_joined_one_with_internet", ["Older", "Newer"] },
        { "two_joined_none_connected", ["Newer", "Older"] },
        { "two_joined_captive_portal", ["Newer", "Older"] },
        { "connected_but_never_joined", ["Older"] },
        { "same_profile_on_two_interfaces", ["Lab"] },
        { "nothing_joined", [] },
        { "wifi_roaming_disabled", [] }
    };

    public static TheoryData<string, Dot1xAuthenticationStatus, bool, bool> WiredScenarios => new()
    {
        // scenario, dot1x, wiredRoaming, expectWired
        { "wired_applied", Dot1xAuthenticationStatus.ProfileApplied, true, true },
        { "wired_authenticated", Dot1xAuthenticationStatus.Authenticated, true, true },
        { "wired_roaming_disabled", Dot1xAuthenticationStatus.Authenticated, false, false },
        { "wired_unavailable", Dot1xAuthenticationStatus.Unavailable, true, false },
        { "wired_not_configured", Dot1xAuthenticationStatus.NotConfigured, true, false }
    };

    [Theory]
    [MemberData(nameof(WifiScenarios))]
    public void Select_WifiScenarioTable(string scenario, string[] expectedProfileNames)
    {
        (NetworkState state, IReadOnlyList<SuccessfulWifiConnection> history, bool wifiRoaming) = WifiScenario(scenario);

        RoamingSelection selection = RoamingSelectionPolicy.Select(state, history, Roaming(wifiRoaming, wiredRoaming: false), wiredDot1xEnabled: false);

        Assert.True(
            expectedProfileNames.SequenceEqual(selection.WifiProfiles.Select(static item => item.ProfileName)),
            $"{scenario}: {string.Join(',', selection.WifiProfiles.Select(static item => item.ProfileName))}");
        Assert.False(selection.CaptureWiredDot1x, scenario);
    }

    [Theory]
    [MemberData(nameof(WiredScenarios))]
    public void Select_WiredScenarioTable(string scenario, Dot1xAuthenticationStatus dot1x, bool wiredRoaming, bool expectWired)
    {
        NetworkState state = State(InternetStatus.Online, ethernetUsable: true, connection: null) with
        {
            Ethernet = new EthernetState([EthernetInterface(usable: true)], new Dot1xState(dot1x))
        };

        RoamingSelection selection = RoamingSelectionPolicy.Select(
            state,
            [],
            Roaming(wifiRoaming: true, wiredRoaming),
            wiredDot1xEnabled: dot1x != Dot1xAuthenticationStatus.NotConfigured);

        Assert.True(expectWired == selection.CaptureWiredDot1x, scenario);
        Assert.Empty(selection.WifiProfiles);
    }

    private static (NetworkState State, IReadOnlyList<SuccessfulWifiConnection> History, bool WifiRoaming) WifiScenario(string scenario)
    {
        SuccessfulWifiConnection lab = Joined(WifiInterface, "Lab", minutesAgo: 5);
        SuccessfulWifiConnection older = Joined(WifiInterface, "Older", minutesAgo: 10);
        SuccessfulWifiConnection newer = Joined(WifiInterface, "Newer", minutesAgo: 2);
        return scenario switch
        {
            "ethernet_active_one_joined" => (State(InternetStatus.Online, ethernetUsable: true, Connection("Lab")), [lab], true),
            "wifi_path_one_joined" => (State(InternetStatus.Online, ethernetUsable: false, Connection("Lab")), [lab], true),
            "two_joined_one_with_internet" => (State(InternetStatus.Online, ethernetUsable: false, Connection("Older")), [older, newer], true),
            "two_joined_none_connected" => (State(InternetStatus.Online, ethernetUsable: true, connection: null), [older, newer], true),
            "two_joined_captive_portal" => (State(InternetStatus.CaptivePortal, ethernetUsable: false, Connection("Older")), [older, newer], true),
            "connected_but_never_joined" => (State(InternetStatus.Online, ethernetUsable: false, Connection("Unjoined")), [older], true),
            "same_profile_on_two_interfaces" => (State(InternetStatus.Online, ethernetUsable: true, connection: null),
                [lab, Joined(UsbWifiInterface, "Lab", minutesAgo: 1)], true),
            "nothing_joined" => (State(InternetStatus.Online, ethernetUsable: false, Connection("Lab")), [], true),
            "wifi_roaming_disabled" => (State(InternetStatus.Online, ethernetUsable: false, Connection("Lab")), [lab], false),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };
    }

    private static NetworkState State(InternetStatus internet, bool ethernetUsable, WifiConnection? connection) => NetworkState.Initial(Now) with
    {
        Internet = internet,
        Ethernet = new EthernetState([EthernetInterface(ethernetUsable)], Dot1xState.NotConfigured),
        Wifi = WifiState.Initial with { Availability = WifiAvailability.Available, Connection = connection }
    };

    private static EthernetInterfaceState EthernetInterface(bool usable) =>
        new("eth0", "Ethernet", usable, usable ? IpConfigurationKind.Dhcp : IpConfigurationKind.None, null, null);

    private static WifiConnection Connection(string name) =>
        new(WifiInterface, WifiSsid.FromText(name), name, WifiSecurityType.Wpa2Personal);

    private static SuccessfulWifiConnection Joined(Guid interfaceId, string name, int minutesAgo) =>
        new(interfaceId, name, WifiSsid.FromText(name), WifiConnectionSource.Manual, Now.AddMinutes(-minutesAgo));

    private static ConnectNetworkProfileRoamingSettings Roaming(bool wifiRoaming, bool wiredRoaming) => new()
    {
        Wifi = new NetworkProfileRoamingTransportSettings { IsEnabled = wifiRoaming },
        WiredDot1x = new NetworkProfileRoamingTransportSettings { IsEnabled = wiredRoaming }
    };
}
