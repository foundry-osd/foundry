// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Sockets;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>Holds a scope as an inclusive numeric address interval within one address family.</summary>
public sealed record ParsedNetworkCacheScope(AddressFamily Family, UInt128 First, UInt128 Last)
{
    /// <summary>Gets the number of addresses in the scope; smaller means more specific.</summary>
    public UInt128 Size => Last - First + 1;

    /// <summary>Tells whether the address is inside the scope. IPv4-mapped IPv6 addresses are compared as IPv4.</summary>
    public bool Contains(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        IPAddress normalized = NetworkCacheScopeParser.Normalize(address);
        if (normalized.AddressFamily != Family) return false;
        UInt128 value = NetworkCacheScopeParser.ToNumber(normalized);
        return value >= First && value <= Last;
    }
}
