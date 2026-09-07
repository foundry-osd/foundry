// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.NetworkInformation;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Models.Network;
using Foundry.Connect.Services.Localization;
using Foundry.Connect.Services.Network;
using Foundry.Utilities.Networking;
using Microsoft.Extensions.Logging;
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
        Assert.True(snapshot.IsDhcpEnabled);
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
        Assert.False(snapshot.IsDhcpEnabled);
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

    [Fact]
    public async Task GetSnapshotAsync_CallerCancellationMustPropagate()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var service = CreateService(new StubNetworkAdapterSnapshotProvider([]));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetSnapshotAsync(cancelled.Token));
    }
    [Theory]
    [InlineData("169.254.1.2", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("127.0.0.2", false)]
    [InlineData("::", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("ff02::1", false)]
    [InlineData("2001:db8::1", true)]
    [InlineData("fd12::1", true)]
    [InlineData("192.0.2.3", true)]
    public async Task GetSnapshotAsync_RequiresUsableAddressBeforeProbing(string address, bool usable)
    {
        var probe = new StubProbe();
        NetworkAdapterSnapshot adapter = address.Contains(':')
            ? CreateAdapter("Ethernet", NetworkInterfaceType.Ethernet, OperationalStatus.Up) with { Ipv6Addresses = [address] }
            : CreateAdapter("Ethernet", NetworkInterfaceType.Ethernet, OperationalStatus.Up, address);
        var service = new NetworkStatusService(new(), new LocalizationService(), NullLogger<NetworkStatusService>.Instance,
            probe, new StubNetworkAdapterSnapshotProvider([adapter]));
        NetworkStatusSnapshot snapshot = await service.GetSnapshotAsync(TestContext.Current.CancellationToken);
        Assert.Equal(usable ? 1 : 0, probe.Calls);
        Assert.Equal(usable, snapshot.HasEthernetUsableAddress);
        Assert.Equal(usable ? NetworkReadinessStatus.Unavailable : NetworkReadinessStatus.NoUsableAddress, snapshot.Readiness.Status);
        if (usable && address.Contains(':')) Assert.Equal(address, snapshot.EthernetIpAddress);
    }
    [Fact]
    public async Task GetSnapshotAsync_PrefersUsableIpv6OverApipaDisplayAddress()
    {
        var adapter = CreateAdapter("Ethernet", NetworkInterfaceType.Ethernet, OperationalStatus.Up, "169.254.1.2")
            with
        { Ipv6Addresses = ["2001:db8::1"] };
        NetworkStatusSnapshot snapshot = await CreateService(new StubNetworkAdapterSnapshotProvider([adapter]))
            .GetSnapshotAsync(TestContext.Current.CancellationToken);
        Assert.True(snapshot.HasEthernetUsableAddress);
        Assert.Equal("2001:db8::1", snapshot.EthernetIpAddress);
    }

    private static NetworkStatusService CreateService(INetworkAdapterSnapshotProvider provider)
    {
        var configuration = new FoundryConnectConfiguration
        {
            InternetProbe = new InternetProbeOptions { Probes = [] }
        };

        return new NetworkStatusService(
            configuration,
            new LocalizationService(),
            NullLogger<NetworkStatusService>.Instance,
            new StubProbe(),
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

    private sealed class StubProbe : INetworkProbeService
    {
        public int Calls { get; private set; }
        public Task<NetworkProbeResult> ProbeAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new NetworkProbeResult(NetworkReadinessStatus.Unavailable, "test_unavailable"));
        }
    }
}
