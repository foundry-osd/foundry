// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;

namespace Foundry.Connect.Tests.Network;

public sealed class NetworkStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(InternetStatus.Online, true, false, InternetPath.Ethernet)]
    [InlineData(InternetStatus.Online, true, true, InternetPath.Ethernet)]
    [InlineData(InternetStatus.Online, false, true, InternetPath.Wifi)]
    [InlineData(InternetStatus.Online, false, false, InternetPath.None)]
    [InlineData(InternetStatus.CaptivePortal, false, true, InternetPath.None)]
    [InlineData(InternetStatus.NoInternet, true, false, InternetPath.None)]
    public void ActiveInternetPath_PrefersUsableEthernet(InternetStatus internet, bool ethernetUsable, bool wifiConnected, InternetPath expected)
    {
        var ethernet = new EthernetInterfaceState(
            "eth0",
            "Ethernet",
            HasLink: true,
            ethernetUsable ? IpConfigurationKind.Dhcp : IpConfigurationKind.Apipa,
            ethernetUsable ? "192.0.2.10" : "169.254.1.1",
            null);
        WifiConnection? connection = wifiConnected
            ? new WifiConnection(Guid.NewGuid(), WifiSsid.FromText("Lab"), "Lab", WifiSecurityType.Wpa2Personal)
            : null;
        NetworkState state = NetworkState.Initial(Now) with
        {
            Ethernet = new EthernetState([ethernet], Dot1xState.NotConfigured),
            Wifi = WifiState.Initial with { Availability = WifiAvailability.Available, Connection = connection },
            Internet = internet
        };

        Assert.Equal(expected, state.ActiveInternetPath);
    }

    [Fact]
    public void Primary_PrefersInterfaceWithUsableAddressThenLink()
    {
        var down = new EthernetInterfaceState("1", "Down", false, IpConfigurationKind.None, null, null);
        var apipa = new EthernetInterfaceState("2", "Apipa", true, IpConfigurationKind.Apipa, "169.254.3.4", null);
        var dhcp = new EthernetInterfaceState("3", "Dhcp", true, IpConfigurationKind.Dhcp, "192.0.2.5", "192.0.2.1");

        Assert.Equal("Dhcp", new EthernetState([down, apipa, dhcp], Dot1xState.NotConfigured).Primary!.Name);
        Assert.Equal("Apipa", new EthernetState([down, apipa], Dot1xState.NotConfigured).Primary!.Name);
        Assert.Equal("Down", new EthernetState([down], Dot1xState.NotConfigured).Primary!.Name);
        Assert.Null(EthernetState.Empty.Primary);
    }

    [Theory]
    [InlineData(Dot1xAuthenticationStatus.NotConfigured, Dot1xFailureReason.None, false)]
    [InlineData(Dot1xAuthenticationStatus.Unavailable, Dot1xFailureReason.SecretUnavailable, false)]
    [InlineData(Dot1xAuthenticationStatus.ProfileApplied, Dot1xFailureReason.None, true)]
    [InlineData(Dot1xAuthenticationStatus.Authenticated, Dot1xFailureReason.None, true)]
    [InlineData(Dot1xAuthenticationStatus.Failed, Dot1xFailureReason.AuthenticationFailed, true)]
    [InlineData(Dot1xAuthenticationStatus.Failed, Dot1xFailureReason.ProfileImportFailed, false)]
    public void Dot1x_IsProfileApplied(Dot1xAuthenticationStatus status, Dot1xFailureReason reason, bool expected)
    {
        Assert.Equal(expected, new Dot1xState(status, reason).IsProfileApplied);
    }
}
