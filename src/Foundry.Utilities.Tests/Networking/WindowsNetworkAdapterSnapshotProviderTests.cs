// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.NetworkInformation;
using System.Net;
using Foundry.Utilities.Networking;

namespace Foundry.Utilities.Tests.Networking;

public sealed class WindowsNetworkAdapterSnapshotProviderTests
{
    [Fact]
    public void CreateIpFacts_MapsIpv4AddressesAndGatewaysWhilePreservingDualStackDns()
    {
        NetworkAdapterIpFacts facts = WindowsNetworkAdapterInfo.CreateIpFacts(
            [
                new NetworkAddressFacts(
                    IPAddress.Parse("192.0.2.10"),
                    IPAddress.Parse("255.255.255.0")),
                new NetworkAddressFacts(IPAddress.Parse("2001:db8::10"), null),
                new NetworkAddressFacts(IPAddress.Parse("198.51.100.10"), null)
            ],
            [IPAddress.Parse("192.0.2.1"), IPAddress.Parse("2001:db8::1")],
            [IPAddress.Parse("192.0.2.53"), IPAddress.Parse("2001:db8::53")],
            isDhcpEnabled: true);

        Assert.Equal(2, facts.Ipv4Addresses.Count);
        Assert.Equal("192.0.2.10", facts.Ipv4Addresses[0].Address);
        Assert.Equal("255.255.255.0", facts.Ipv4Addresses[0].SubnetMask);
        Assert.Equal("198.51.100.10", facts.Ipv4Addresses[1].Address);
        Assert.Null(facts.Ipv4Addresses[1].SubnetMask);
        Assert.Equal(["192.0.2.1"], facts.Gateways);
        Assert.Equal(["192.0.2.53", "2001:db8::53"], facts.DnsServers);
        Assert.True(facts.IsDhcpEnabled);
    }

    [Fact]
    public void GetAdapters_MapsFactsAndPreservesSourceOrder()
    {
        var ethernet = new StubNetworkAdapterInfo
        {
            Id = "ethernet-id",
            Name = "Ethernet",
            InterfaceType = NetworkInterfaceType.Ethernet,
            OperationalStatus = OperationalStatus.Up,
            PhysicalAddressBytes = [0x00, 0x11, 0xAA, 0xBB, 0xCC, 0xDD],
            IpFacts = new NetworkAdapterIpFacts(
                [new NetworkIpv4AddressSnapshot("192.0.2.10", "255.255.255.0")],
                ["192.0.2.1"],
                ["192.0.2.53", "2001:db8::53"],
                true)
        };
        var loopback = new StubNetworkAdapterInfo
        {
            Id = "loopback-id",
            Name = "Loopback",
            InterfaceType = NetworkInterfaceType.Loopback,
            OperationalStatus = OperationalStatus.Up
        };
        var provider = new WindowsNetworkAdapterSnapshotProvider(() => [ethernet, loopback]);

        IReadOnlyList<NetworkAdapterSnapshot> adapters = provider.GetAdapters();

        Assert.Equal(["ethernet-id", "loopback-id"], adapters.Select(static adapter => adapter.Id));
        NetworkAdapterSnapshot snapshot = adapters[0];
        Assert.Equal("Ethernet", snapshot.Name);
        Assert.Equal(NetworkInterfaceType.Ethernet, snapshot.InterfaceType);
        Assert.Equal(OperationalStatus.Up, snapshot.OperationalStatus);
        Assert.Equal("00-11-AA-BB-CC-DD", snapshot.MacAddress);
        NetworkIpv4AddressSnapshot address = Assert.Single(snapshot.Ipv4Addresses);
        Assert.Equal("192.0.2.10", address.Address);
        Assert.Equal("255.255.255.0", address.SubnetMask);
        Assert.Equal(["192.0.2.1"], snapshot.Gateways);
        Assert.Equal(["192.0.2.53", "2001:db8::53"], snapshot.DnsServers);
        Assert.True(snapshot.IsDhcpEnabled);
    }

    [Fact]
    public void GetAdapters_WhenOptionalFactsFail_RetainsAdapterWithNeutralFacts()
    {
        var adapter = new StubNetworkAdapterInfo
        {
            Id = "ethernet-id",
            Name = "Ethernet",
            InterfaceType = NetworkInterfaceType.Ethernet,
            OperationalStatus = OperationalStatus.Up,
            ThrowOnPhysicalAddress = true,
            ThrowOnIpFacts = true
        };
        var provider = new WindowsNetworkAdapterSnapshotProvider(() => [adapter]);

        NetworkAdapterSnapshot snapshot = Assert.Single(provider.GetAdapters());

        Assert.Equal(string.Empty, snapshot.MacAddress);
        Assert.Empty(snapshot.Ipv4Addresses);
        Assert.Empty(snapshot.Gateways);
        Assert.Empty(snapshot.DnsServers);
        Assert.False(snapshot.IsDhcpEnabled);
    }

    [Fact]
    public void GetAdapters_WhenOneAdapterIdentityFails_ContinuesWithOtherAdapters()
    {
        var invalid = new StubNetworkAdapterInfo { ThrowOnIdentity = true };
        var valid = new StubNetworkAdapterInfo
        {
            Id = "valid-id",
            Name = "Ethernet",
            InterfaceType = NetworkInterfaceType.Ethernet,
            OperationalStatus = OperationalStatus.Down
        };
        var provider = new WindowsNetworkAdapterSnapshotProvider(() => [invalid, valid]);

        NetworkAdapterSnapshot snapshot = Assert.Single(provider.GetAdapters());

        Assert.Equal("valid-id", snapshot.Id);
    }

    [Fact]
    public void GetAdapters_WhenEnumerationFails_PropagatesFailure()
    {
        var provider = new WindowsNetworkAdapterSnapshotProvider(
            () => throw new NetworkInformationException());

        Assert.Throws<NetworkInformationException>(() => provider.GetAdapters());
    }

    private sealed class StubNetworkAdapterInfo : IWindowsNetworkAdapterInfo
    {
        private string _id = string.Empty;

        public string Id
        {
            get => ThrowOnIdentity ? throw new InvalidOperationException() : _id;
            init => _id = value;
        }

        public string Name { get; init; } = string.Empty;

        public NetworkInterfaceType InterfaceType { get; init; }

        public OperationalStatus OperationalStatus { get; init; }

        public byte[] PhysicalAddressBytes { get; init; } = [];

        public NetworkAdapterIpFacts IpFacts { get; init; } = NetworkAdapterIpFacts.Empty;

        public bool ThrowOnIdentity { get; init; }

        public bool ThrowOnPhysicalAddress { get; init; }

        public bool ThrowOnIpFacts { get; init; }

        public byte[] GetPhysicalAddressBytes()
            => ThrowOnPhysicalAddress ? throw new InvalidOperationException() : PhysicalAddressBytes;

        public NetworkAdapterIpFacts GetIpFacts()
            => ThrowOnIpFacts ? throw new InvalidOperationException() : IpFacts;
    }
}
