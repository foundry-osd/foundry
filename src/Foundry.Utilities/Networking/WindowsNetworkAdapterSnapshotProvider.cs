// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Foundry.Utilities.Networking;

/// <summary>
/// Reads network adapter facts from Windows.
/// </summary>
public sealed class WindowsNetworkAdapterSnapshotProvider : INetworkAdapterSnapshotProvider
{
    private readonly Func<IEnumerable<IWindowsNetworkAdapterInfo>> _enumerateAdapters;

    /// <summary>
    /// Initializes a provider backed by <see cref="NetworkInterface"/>.
    /// </summary>
    public WindowsNetworkAdapterSnapshotProvider()
        : this(EnumerateWindowsAdapters)
    {
    }

    internal WindowsNetworkAdapterSnapshotProvider(
        Func<IEnumerable<IWindowsNetworkAdapterInfo>> enumerateAdapters)
    {
        ArgumentNullException.ThrowIfNull(enumerateAdapters);
        _enumerateAdapters = enumerateAdapters;
    }

    /// <inheritdoc />
    public IReadOnlyList<NetworkAdapterSnapshot> GetAdapters()
    {
        var snapshots = new List<NetworkAdapterSnapshot>();
        foreach (IWindowsNetworkAdapterInfo adapter in _enumerateAdapters())
        {
            string id;
            string name;
            NetworkInterfaceType interfaceType;
            OperationalStatus operationalStatus;

            try
            {
                id = adapter.Id;
                name = adapter.Name;
                interfaceType = adapter.InterfaceType;
                operationalStatus = adapter.OperationalStatus;
            }
            catch (Exception exception) when (IsAdapterAccessException(exception))
            {
                continue;
            }

            string macAddress = string.Empty;
            try
            {
                macAddress = FormatMacAddress(adapter.GetPhysicalAddressBytes());
            }
            catch (Exception exception) when (IsAdapterAccessException(exception))
            {
            }

            NetworkAdapterIpFacts ipFacts = NetworkAdapterIpFacts.Empty;
            try
            {
                ipFacts = adapter.GetIpFacts();
            }
            catch (Exception exception) when (IsAdapterAccessException(exception))
            {
            }

            snapshots.Add(new NetworkAdapterSnapshot(
                id,
                name,
                interfaceType,
                operationalStatus,
                macAddress,
                ipFacts.Ipv4Addresses,
                ipFacts.Gateways,
                ipFacts.DnsServers,
                ipFacts.IsDhcpEnabled));
        }

        return snapshots;
    }

    private static IEnumerable<IWindowsNetworkAdapterInfo> EnumerateWindowsAdapters()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Select(static adapter => (IWindowsNetworkAdapterInfo)new WindowsNetworkAdapterInfo(adapter));
    }

    private static string FormatMacAddress(byte[] addressBytes)
    {
        return addressBytes.Length == 0
            ? string.Empty
            : string.Join("-", addressBytes.Select(static value => value.ToString("X2", CultureInfo.InvariantCulture)));
    }

    private static bool IsAdapterAccessException(Exception exception)
    {
        return exception is NetworkInformationException
            or InvalidOperationException
            or ObjectDisposedException
            or PlatformNotSupportedException;
    }
}

internal interface IWindowsNetworkAdapterInfo
{
    string Id { get; }

    string Name { get; }

    NetworkInterfaceType InterfaceType { get; }

    OperationalStatus OperationalStatus { get; }

    byte[] GetPhysicalAddressBytes();

    NetworkAdapterIpFacts GetIpFacts();
}

internal sealed record NetworkAdapterIpFacts(
    IReadOnlyList<NetworkIpv4AddressSnapshot> Ipv4Addresses,
    IReadOnlyList<string> Gateways,
    IReadOnlyList<string> DnsServers,
    bool IsDhcpEnabled)
{
    public static NetworkAdapterIpFacts Empty { get; } = new([], [], [], false);
}

internal sealed class WindowsNetworkAdapterInfo(NetworkInterface adapter) : IWindowsNetworkAdapterInfo
{
    public string Id => adapter.Id;

    public string Name => adapter.Name;

    public NetworkInterfaceType InterfaceType => adapter.NetworkInterfaceType;

    public OperationalStatus OperationalStatus => adapter.OperationalStatus;

    public byte[] GetPhysicalAddressBytes() => adapter.GetPhysicalAddress().GetAddressBytes();

    public NetworkAdapterIpFacts GetIpFacts()
    {
        IPInterfaceProperties properties = adapter.GetIPProperties();
        NetworkAddressFacts[] addresses = properties.UnicastAddresses
            .Select(static address => new NetworkAddressFacts(
                address.Address,
                address.Address.AddressFamily == AddressFamily.InterNetwork ? address.IPv4Mask : null))
            .ToArray();
        IPAddress[] gateways = properties.GatewayAddresses
            .Select(static gateway => gateway.Address)
            .ToArray();
        IPAddress[] dnsServers = [.. properties.DnsAddresses];

        bool isDhcpEnabled;
        try
        {
            isDhcpEnabled = properties.GetIPv4Properties()?.IsDhcpEnabled == true;
        }
        catch (Exception exception) when (exception is NetworkInformationException
                                              or InvalidOperationException
                                              or PlatformNotSupportedException)
        {
            isDhcpEnabled = false;
        }

        return CreateIpFacts(addresses, gateways, dnsServers, isDhcpEnabled);
    }

    internal static NetworkAdapterIpFacts CreateIpFacts(
        IEnumerable<NetworkAddressFacts> addresses,
        IEnumerable<IPAddress> gateways,
        IEnumerable<IPAddress> dnsServers,
        bool isDhcpEnabled)
    {
        NetworkIpv4AddressSnapshot[] ipv4Addresses = addresses
            .Where(static address => address.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(static address => new NetworkIpv4AddressSnapshot(
                address.Address.ToString(),
                address.SubnetMask?.ToString()))
            .ToArray();
        string[] ipv4Gateways = gateways
            .Where(static gateway => gateway.AddressFamily == AddressFamily.InterNetwork)
            .Select(static gateway => gateway.ToString())
            .ToArray();
        string[] mappedDnsServers = dnsServers
            .Select(static address => address.ToString())
            .ToArray();

        return new NetworkAdapterIpFacts(
            ipv4Addresses,
            ipv4Gateways,
            mappedDnsServers,
            isDhcpEnabled);
    }
}

internal sealed record NetworkAddressFacts(IPAddress Address, IPAddress? SubnetMask);
