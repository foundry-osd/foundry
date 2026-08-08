// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.NetworkInformation;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Models.Network;
using Foundry.Connect.Services.Localization;
using Foundry.Connect.Services.Network;
using Foundry.Utilities.Networking;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Connect.Tests;

public sealed class NetworkStatusServiceTests
{
    [Fact]
    public async Task GetSnapshotAsync_SelectsFirstConnectedEthernetAdapter()
    {
        NetworkAdapterSnapshot[] adapters =
        [
            CreateAdapter("Loopback", NetworkInterfaceType.Loopback, OperationalStatus.Up, "127.0.0.1"),
            CreateAdapter("Disconnected", NetworkInterfaceType.Ethernet, OperationalStatus.Down, "192.0.2.5"),
            CreateAdapter(
                "Connected",
                NetworkInterfaceType.GigabitEthernet,
                OperationalStatus.Up,
                "192.0.2.10",
                gateway: "192.0.2.1",
                isDhcpEnabled: true),
            CreateAdapter("Wi-Fi", NetworkInterfaceType.Wireless80211, OperationalStatus.Down)
        ];
        NetworkStatusService service = CreateService(new StubNetworkAdapterSnapshotProvider(adapters));

        NetworkStatusSnapshot snapshot = await service.GetSnapshotAsync(TestContext.Current.CancellationToken);

        Assert.True(snapshot.HasEthernetAdapter);
        Assert.True(snapshot.IsEthernetConnected);
        Assert.True(snapshot.HasDhcpLease);
        Assert.True(snapshot.HasEthernetIpv4);
        Assert.True(snapshot.HasWirelessAdapter);
        Assert.False(snapshot.IsWifiRuntimeAvailable);
        Assert.False(snapshot.HasInternetAccess);
        Assert.Equal("Connected", snapshot.EthernetAdapterName);
        Assert.Equal("192.0.2.10", snapshot.EthernetIpAddress);
        Assert.Equal("192.0.2.1", snapshot.EthernetGateway);
    }

    [Fact]
    public async Task GetSnapshotAsync_WhenEthernetIsDisconnected_UsesFirstAdapterForDisplay()
    {
        NetworkStatusService service = CreateService(new StubNetworkAdapterSnapshotProvider(
        [
            CreateAdapter("First", NetworkInterfaceType.Ethernet, OperationalStatus.Down, "198.51.100.5"),
            CreateAdapter("Second", NetworkInterfaceType.Ethernet, OperationalStatus.Down, "198.51.100.6")
        ]));

        NetworkStatusSnapshot snapshot = await service.GetSnapshotAsync(TestContext.Current.CancellationToken);

        Assert.False(snapshot.IsEthernetConnected);
        Assert.False(snapshot.HasDhcpLease);
        Assert.Equal("First", snapshot.EthernetAdapterName);
        Assert.Equal("198.51.100.5", snapshot.EthernetIpAddress);
    }

    [Fact]
    public async Task GetSnapshotAsync_WhenProviderFails_PropagatesFailure()
    {
        NetworkStatusService service = CreateService(new StubNetworkAdapterSnapshotProvider(
            () => throw new NetworkInformationException()));

        await Assert.ThrowsAsync<NetworkInformationException>(
            () => service.GetSnapshotAsync(TestContext.Current.CancellationToken));
    }

    private static NetworkStatusService CreateService(INetworkAdapterSnapshotProvider provider)
    {
        var configuration = new FoundryConnectConfiguration
        {
            InternetProbe = new InternetProbeOptions { ProbeUris = [] }
        };

        return new NetworkStatusService(
            configuration,
            new LocalizationService(),
            NullLogger<NetworkStatusService>.Instance,
            provider);
    }

    private static NetworkAdapterSnapshot CreateAdapter(
        string name,
        NetworkInterfaceType interfaceType,
        OperationalStatus operationalStatus,
        string? ipv4Address = null,
        string? gateway = null,
        bool isDhcpEnabled = false)
    {
        return new NetworkAdapterSnapshot(
            name,
            name,
            interfaceType,
            operationalStatus,
            string.Empty,
            ipv4Address is null ? [] : [new NetworkIpv4AddressSnapshot(ipv4Address, "255.255.255.0")],
            gateway is null ? [] : [gateway],
            [],
            isDhcpEnabled);
    }

    private sealed class StubNetworkAdapterSnapshotProvider : INetworkAdapterSnapshotProvider
    {
        private readonly Func<IReadOnlyList<NetworkAdapterSnapshot>> _getAdapters;

        public StubNetworkAdapterSnapshotProvider(IReadOnlyList<NetworkAdapterSnapshot> adapters)
            : this(() => adapters)
        {
        }

        public StubNetworkAdapterSnapshotProvider(Func<IReadOnlyList<NetworkAdapterSnapshot>> getAdapters)
        {
            _getAdapters = getAdapters;
        }

        public IReadOnlyList<NetworkAdapterSnapshot> GetAdapters() => _getAdapters();
    }
}
