// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.NetworkInformation;
using Foundry.Deploy.Services.Network;
using Foundry.Utilities.Networking;
using Foundry.Wpf.Network;

namespace Foundry.Deploy.Tests;

public sealed class DeployNetworkIndicatorTests
{
    public static TheoryData<NetworkAdapterSnapshot[], NetworkIndicatorState> Mappings => new()
    {
        { [], NetworkIndicatorState.Disconnected },
        { [Adapter(NetworkInterfaceType.Ethernet)], NetworkIndicatorState.Ethernet },
        { [Adapter(NetworkInterfaceType.GigabitEthernet)], NetworkIndicatorState.Ethernet },
        { [Adapter(NetworkInterfaceType.Wireless80211)], NetworkIndicatorState.Wifi },
        { [Adapter(NetworkInterfaceType.Wireless80211), Adapter(NetworkInterfaceType.Ethernet)], NetworkIndicatorState.Ethernet },
        // An Ethernet adapter without a routable address (cable without DHCP: APIPA only) does not hide a connected Wi-Fi.
        { [Adapter(NetworkInterfaceType.Ethernet, addresses: []), Adapter(NetworkInterfaceType.Wireless80211)], NetworkIndicatorState.Wifi },
        { [Adapter(NetworkInterfaceType.Ethernet, OperationalStatus.Down)], NetworkIndicatorState.Disconnected },
        { [Adapter(NetworkInterfaceType.Wireless80211, OperationalStatus.Dormant)], NetworkIndicatorState.Disconnected },
        { [Adapter(NetworkInterfaceType.Loopback), Adapter(NetworkInterfaceType.Tunnel)], NetworkIndicatorState.Disconnected }
    };

    [Theory]
    [MemberData(nameof(Mappings))]
    public void Map_ReadsTheFirstConnectedWiredThenWirelessAdapter(NetworkAdapterSnapshot[] adapters, NetworkIndicatorState expected)
    {
        Assert.Equal(expected, DeployNetworkIndicator.Map(adapters));
    }

    [Fact]
    public void Start_ReadsTheStateAtOnce()
    {
        var adapters = new SwitchableAdapters([Adapter(NetworkInterfaceType.Ethernet)]);
        using var indicator = new DeployNetworkIndicator(adapters, new ManualTimeProvider());

        indicator.Start();

        Assert.Equal(NetworkIndicatorState.Ethernet, indicator.State);
        Assert.Equal(1, adapters.Reads);
    }

    [Fact]
    public void Polling_EveryFiveSeconds_RaisesTheEventOnlyWhenTheStateChanges()
    {
        var adapters = new SwitchableAdapters([Adapter(NetworkInterfaceType.Ethernet)]);
        var clock = new ManualTimeProvider();
        using var indicator = new DeployNetworkIndicator(adapters, clock);
        int changes = 0;
        indicator.StateChanged += (_, _) => changes++;
        indicator.Start();

        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(1, adapters.Reads);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, adapters.Reads);
        Assert.Equal(0, changes);

        adapters.Current = [Adapter(NetworkInterfaceType.Wireless80211)];
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(1, changes);
        Assert.Equal(NetworkIndicatorState.Wifi, indicator.State);
    }

    [Fact]
    public void Polling_WhenTheAdaptersCannotBeRead_KeepsTheLastState()
    {
        var adapters = new SwitchableAdapters([Adapter(NetworkInterfaceType.Ethernet)]);
        var clock = new ManualTimeProvider();
        using var indicator = new DeployNetworkIndicator(adapters, clock);
        indicator.Start();

        adapters.Failure = new NetworkInformationException();
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(NetworkIndicatorState.Ethernet, indicator.State);
    }

    [Fact]
    public void Dispose_StopsPolling()
    {
        var adapters = new SwitchableAdapters([Adapter(NetworkInterfaceType.Ethernet)]);
        var clock = new ManualTimeProvider();
        var indicator = new DeployNetworkIndicator(adapters, clock);
        indicator.Start();

        indicator.Dispose();
        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(1, adapters.Reads);
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    private static NetworkAdapterSnapshot Adapter(
        NetworkInterfaceType type,
        OperationalStatus status = OperationalStatus.Up,
        string[]? addresses = null) =>
        new("id", "adapter", type, status, string.Empty, [], [], [], IsDhcpEnabled: true)
        {
            UnicastAddresses = addresses ?? ["10.0.0.5"]
        };

    private sealed class SwitchableAdapters(NetworkAdapterSnapshot[] current) : INetworkAdapterSnapshotProvider
    {
        public NetworkAdapterSnapshot[] Current { get; set; } = current;

        public Exception? Failure { get; set; }

        public int Reads { get; private set; }

        public IReadOnlyList<NetworkAdapterSnapshot> GetAdapters()
        {
            Reads++;
            return Failure is null ? Current : throw Failure;
        }
    }
}
