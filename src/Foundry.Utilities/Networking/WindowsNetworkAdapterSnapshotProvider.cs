// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
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
        NetworkIpv4AddressSnapshot[] ipv4Addresses = properties.UnicastAddresses
            .Where(static address => address.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(static address => new NetworkIpv4AddressSnapshot(
                address.Address.ToString(),
                address.IPv4Mask?.ToString()))
            .ToArray();
        string[] gateways = properties.GatewayAddresses
            .Where(static gateway => gateway.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(static gateway => gateway.Address.ToString())
            .ToArray();
        string[] dnsServers = properties.DnsAddresses
            .Select(static address => address.ToString())
            .ToArray();

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

        return new NetworkAdapterIpFacts(ipv4Addresses, gateways, dnsServers, isDhcpEnabled);
    }
}
