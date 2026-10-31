// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Foundry.Utilities.Networking;

namespace Foundry.Connect.Network;

/// <summary>
/// Turns raw adapter snapshots into the Ethernet part of the network state.
/// </summary>
public static class EthernetStateReader
{
    /// <summary>
    /// Returns whether an interface type is a wired Ethernet family.
    /// </summary>
    /// <param name="type">Interface type.</param>
    /// <returns><see langword="true"/> for Ethernet types.</returns>
    public static bool IsEthernet(NetworkInterfaceType type)
    {
        return type is NetworkInterfaceType.Ethernet
            or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.FastEthernetFx
            or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.Ethernet3Megabit;
    }

    /// <summary>
    /// Reads the Ethernet adapters in input order.
    /// </summary>
    /// <param name="adapters">Adapters in platform enumeration order.</param>
    /// <returns>
    /// One state per Ethernet adapter. The address is the first non-APIPA IPv4 address, else the first IPv4 address;
    /// the link is <see cref="OperationalStatus.Up"/>.
    /// </returns>
    public static IReadOnlyList<EthernetInterfaceState> Read(IReadOnlyList<NetworkAdapterSnapshot> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        return adapters
            .Where(static adapter => IsEthernet(adapter.InterfaceType))
            .Select(static adapter => new EthernetInterfaceState(
                adapter.Id,
                adapter.Name,
                adapter.OperationalStatus == OperationalStatus.Up,
                Classify(adapter),
                SelectAddress(adapter),
                adapter.Gateways.FirstOrDefault()))
            .ToArray();
    }

    /// <summary>
    /// Classifies how an adapter obtained its IPv4 address.
    /// </summary>
    /// <param name="adapter">Adapter snapshot.</param>
    /// <returns>
    /// <see cref="IpConfigurationKind.None"/> without IPv4, <see cref="IpConfigurationKind.Apipa"/> when every IPv4
    /// address is link-local, else <see cref="IpConfigurationKind.Dhcp"/> or <see cref="IpConfigurationKind.Static"/>.
    /// </returns>
    public static IpConfigurationKind Classify(NetworkAdapterSnapshot adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        if (adapter.Ipv4Addresses.Count == 0)
        {
            return IpConfigurationKind.None;
        }

        if (adapter.Ipv4Addresses.All(static address => IsApipa(address.Address)))
        {
            return IpConfigurationKind.Apipa;
        }

        return adapter.IsDhcpEnabled ? IpConfigurationKind.Dhcp : IpConfigurationKind.Static;
    }

    private static string? SelectAddress(NetworkAdapterSnapshot adapter)
    {
        return adapter.Ipv4Addresses.FirstOrDefault(static address => !IsApipa(address.Address))?.Address
            ?? adapter.Ipv4Addresses.FirstOrDefault()?.Address;
    }

    private static bool IsApipa(string address)
    {
        if (!IPAddress.TryParse(address, out IPAddress? parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        byte[] bytes = parsed.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }
}
