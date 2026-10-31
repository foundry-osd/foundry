// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.NetworkInformation;
using Foundry.Connect.Network;
using Foundry.Utilities.Networking;

namespace Foundry.Connect.Tests.Network;

public sealed class EthernetStateReaderTests
{
    private static NetworkAdapterSnapshot Adapter(string name, NetworkInterfaceType type, OperationalStatus status, string? ipv4 = null, bool dhcp = false, string? gateway = null) =>
        new(name, name, type, status, "00-11-22-33-44-55",
            ipv4 is null ? [] : [new NetworkIpv4AddressSnapshot(ipv4, "255.255.255.0")],
            gateway is null ? [] : [gateway], [], dhcp);

    [Fact]
    public void Read_KeepsOnlyEthernetAdaptersInOrder()
    {
        IReadOnlyList<EthernetInterfaceState> result = EthernetStateReader.Read(
        [
            Adapter("Loopback", NetworkInterfaceType.Loopback, OperationalStatus.Up, "127.0.0.1"),
            Adapter("Ethernet", NetworkInterfaceType.Ethernet, OperationalStatus.Up, "192.0.2.10", dhcp: true, gateway: "192.0.2.1"),
            Adapter("Wi-Fi", NetworkInterfaceType.Wireless80211, OperationalStatus.Up, "192.0.2.20"),
            Adapter("Ethernet 2", NetworkInterfaceType.GigabitEthernet, OperationalStatus.Down)
        ]);

        Assert.Equal(["Ethernet", "Ethernet 2"], result.Select(static item => item.Name));
        Assert.Equal(new EthernetInterfaceState("Ethernet", "Ethernet", true, IpConfigurationKind.Dhcp, "192.0.2.10", "192.0.2.1"), result[0]);
        Assert.False(result[1].HasLink);
    }

    [Fact]
    public void Read_PrefersRoutableAddressOverApipa()
    {
        NetworkAdapterSnapshot adapter = new("eth", "Ethernet", NetworkInterfaceType.Ethernet, OperationalStatus.Up, "00-11-22-33-44-55",
            [new NetworkIpv4AddressSnapshot("169.254.10.20", "255.255.0.0"), new NetworkIpv4AddressSnapshot("192.0.2.10", "255.255.255.0")],
            [], [], true);

        EthernetInterfaceState state = Assert.Single(EthernetStateReader.Read([adapter]));

        Assert.Equal("192.0.2.10", state.Ipv4Address);
        Assert.Equal(IpConfigurationKind.Dhcp, state.IpConfiguration);
    }

    [Theory]
    [InlineData(null, false, IpConfigurationKind.None)]
    [InlineData("169.254.10.20", true, IpConfigurationKind.Apipa)]
    [InlineData("192.0.2.10", true, IpConfigurationKind.Dhcp)]
    [InlineData("192.0.2.10", false, IpConfigurationKind.Static)]
    public void Classify_DistinguishesApipaDhcpAndStatic(string? ipv4, bool dhcp, IpConfigurationKind expected)
    {
        Assert.Equal(expected, EthernetStateReader.Classify(Adapter("Ethernet", NetworkInterfaceType.Ethernet, OperationalStatus.Up, ipv4, dhcp)));
    }
}
