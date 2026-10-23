// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>Parses authored network cache scopes; used by Foundry OSD validation and by Deploy share selection.</summary>
public static class NetworkCacheScopeParser
{
    /// <summary>Parses <paramref name="value"/> as <paramref name="kind"/>; surrounding whitespace is ignored.</summary>
    public static bool TryParse(NetworkCacheScopeKind kind, string? value, [NotNullWhen(true)] out ParsedNetworkCacheScope? scope)
    {
        string text = value?.Trim() ?? string.Empty;
        scope = text.Length == 0
            ? null
            : kind switch
            {
                NetworkCacheScopeKind.Subnet => ParseSubnet(text),
                NetworkCacheScopeKind.Range => ParseRange(text),
                NetworkCacheScopeKind.Address => ParseAddress(text),
                _ => null
            };
        return scope is not null;
    }

    internal static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    internal static UInt128 ToNumber(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return bytes.Length == 4
            ? BinaryPrimitives.ReadUInt32BigEndian(bytes)
            : BinaryPrimitives.ReadUInt128BigEndian(bytes);
    }

    private static ParsedNetworkCacheScope? ParseSubnet(string text)
    {
        if (!text.Contains('/') || !IPNetwork.TryParse(text, out IPNetwork network)) return null;
        AddressFamily family = network.BaseAddress.AddressFamily;
        int hostBits = (family == AddressFamily.InterNetwork ? 32 : 128) - network.PrefixLength;
        UInt128 first = ToNumber(network.BaseAddress);
        UInt128 hostMask = hostBits == 128 ? UInt128.MaxValue : (UInt128.One << hostBits) - 1;
        return new(family, first, first | hostMask);
    }

    private static ParsedNetworkCacheScope? ParseRange(string text)
    {
        int separator = text.IndexOf('-');
        if (separator <= 0 ||
            !TryParseAddress(text[..separator].Trim(), out IPAddress? first) ||
            !TryParseAddress(text[(separator + 1)..].Trim(), out IPAddress? last) ||
            first.AddressFamily != last.AddressFamily)
        {
            return null;
        }

        UInt128 start = ToNumber(first);
        UInt128 end = ToNumber(last);
        return start <= end ? new(first.AddressFamily, start, end) : null;
    }

    private static ParsedNetworkCacheScope? ParseAddress(string text)
    {
        if (!TryParseAddress(text, out IPAddress? address)) return null;
        UInt128 value = ToNumber(address);
        return new(address.AddressFamily, value, value);
    }

    private static bool TryParseAddress(string text, [NotNullWhen(true)] out IPAddress? address)
    {
        address = null;
        if (text.Contains('/') || !IPAddress.TryParse(text, out IPAddress? parsed)) return false;
        address = Normalize(parsed);
        return true;
    }
}
