// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.NetworkInformation;

namespace Foundry.Utilities.Networking;

/// <summary>
/// Describes raw Windows network adapter facts.
/// </summary>
public sealed record NetworkAdapterSnapshot(
    string Id,
    string Name,
    NetworkInterfaceType InterfaceType,
    OperationalStatus OperationalStatus,
    string MacAddress,
    IReadOnlyList<NetworkIpv4AddressSnapshot> Ipv4Addresses,
    IReadOnlyList<string> Gateways,
    IReadOnlyList<string> DnsServers,
    bool IsDhcpEnabled)
{
    /// <summary>Gets IPv6 unicast addresses reported by the adapter.</summary>
    public IReadOnlyList<string> Ipv6Addresses { get; init; } = [];
}
