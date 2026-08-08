// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.NetworkInformation;
using Foundry.Deploy.ViewModels;
using Foundry.Utilities.Networking;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentSessionNetworkSnapshotTests
{
    [Fact]
    public void SelectNetworkAdapter_UsesFirstConnectedNonVirtualAdapterWithIpv4()
    {
        NetworkAdapterSnapshot[] adapters =
        [
            CreateAdapter("Loopback", NetworkInterfaceType.Loopback, OperationalStatus.Up, hasIpv4: true),
            CreateAdapter("Down", NetworkInterfaceType.Ethernet, OperationalStatus.Down, hasIpv4: true),
            CreateAdapter("No address", NetworkInterfaceType.Ethernet, OperationalStatus.Up, hasIpv4: false),
            CreateAdapter("Selected", NetworkInterfaceType.Ethernet, OperationalStatus.Up, hasIpv4: true),
            CreateAdapter("Later", NetworkInterfaceType.Ethernet, OperationalStatus.Up, hasIpv4: true)
        ];

        NetworkAdapterSnapshot? adapter = DeploymentSessionViewModel.SelectNetworkAdapter(adapters);

        Assert.Equal("Selected", adapter?.Name);
    }

    [Fact]
    public void SelectNetworkAdapter_WhenNoAdapterMatches_ReturnsNull()
    {
        NetworkAdapterSnapshot[] adapters =
        [
            CreateAdapter("Tunnel", NetworkInterfaceType.Tunnel, OperationalStatus.Up, hasIpv4: true),
            CreateAdapter("Down", NetworkInterfaceType.Ethernet, OperationalStatus.Down, hasIpv4: true)
        ];

        NetworkAdapterSnapshot? adapter = DeploymentSessionViewModel.SelectNetworkAdapter(adapters);

        Assert.Null(adapter);
    }

    private static NetworkAdapterSnapshot CreateAdapter(
        string name,
        NetworkInterfaceType interfaceType,
        OperationalStatus operationalStatus,
        bool hasIpv4)
    {
        return new NetworkAdapterSnapshot(
            name,
            name,
            interfaceType,
            operationalStatus,
            "00-11-22-33-44-55",
            hasIpv4 ? [new NetworkIpv4AddressSnapshot("192.0.2.10", "255.255.255.0")] : [],
            ["192.0.2.1"],
            [],
            true);
    }
}
