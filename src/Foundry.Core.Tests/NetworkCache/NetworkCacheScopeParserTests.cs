// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.NetworkCache;

namespace Foundry.Core.Tests.NetworkCache;

public sealed class NetworkCacheScopeParserTests
{
    [Fact]
    public void Parse_Ipv4Subnet_ContainsHostsOnly()
    {
        Assert.True(NetworkCacheScopeParser.TryParse(NetworkCacheScopeKind.Subnet, "10.10.0.0/16", out ParsedNetworkCacheScope? scope));
        Assert.NotNull(scope);

        Assert.True(scope.Contains(IPAddress.Parse("10.10.255.1")));
        Assert.False(scope.Contains(IPAddress.Parse("10.11.0.1")));
        Assert.Equal((UInt128)65536, scope.Size);
    }

    [Fact]
    public void Parse_IPv6SubnetWithWhitespace_Normalizes()
    {
        Assert.True(NetworkCacheScopeParser.TryParse(NetworkCacheScopeKind.Subnet, "  FD00:10::/48 ", out ParsedNetworkCacheScope? scope));
        Assert.NotNull(scope);

        Assert.True(scope.Contains(IPAddress.Parse("fd00:10:0:1::5")));
        Assert.False(scope.Contains(IPAddress.Parse("fd00:11::1")));
    }

    [Theory]
    [InlineData("10.0.0.9-10.0.0.1")]
    [InlineData("10.0.0.1-fd00::1")]
    [InlineData("10.0.0.1")]
    [InlineData("10.0.0.1-")]
    public void Parse_Range_RejectsReversedOrMixedFamily(string value)
    {
        Assert.False(NetworkCacheScopeParser.TryParse(NetworkCacheScopeKind.Range, value, out _));
    }

    [Fact]
    public void Parse_Range_IncludesBothBounds()
    {
        Assert.True(NetworkCacheScopeParser.TryParse(NetworkCacheScopeKind.Range, "10.20.0.10 - 10.20.0.200", out ParsedNetworkCacheScope? scope));
        Assert.NotNull(scope);

        Assert.True(scope.Contains(IPAddress.Parse("10.20.0.10")));
        Assert.True(scope.Contains(IPAddress.Parse("10.20.0.200")));
        Assert.False(scope.Contains(IPAddress.Parse("10.20.0.201")));
        Assert.Equal((UInt128)191, scope.Size);
    }

    [Fact]
    public void Parse_Address_MatchesOnlyItself()
    {
        Assert.True(NetworkCacheScopeParser.TryParse(NetworkCacheScopeKind.Address, "192.168.1.20", out ParsedNetworkCacheScope? scope));
        Assert.NotNull(scope);

        Assert.True(scope.Contains(IPAddress.Parse("192.168.1.20")));
        Assert.False(scope.Contains(IPAddress.Parse("192.168.1.21")));
        Assert.Equal((UInt128)1, scope.Size);
    }

    [Fact]
    public void Parse_Ipv4MappedIpv6Address_MatchesIpv4Scope()
    {
        Assert.True(NetworkCacheScopeParser.TryParse(NetworkCacheScopeKind.Subnet, "10.10.0.0/16", out ParsedNetworkCacheScope? scope));
        Assert.NotNull(scope);

        Assert.True(scope.Contains(IPAddress.Parse("::ffff:10.10.0.5")));
    }

    [Theory]
    [InlineData(NetworkCacheScopeKind.Subnet, "10.10.0.0")]
    [InlineData(NetworkCacheScopeKind.Subnet, "not-an-address/8")]
    [InlineData(NetworkCacheScopeKind.Address, "")]
    [InlineData(NetworkCacheScopeKind.Address, "10.0.0.0/8")]
    public void Parse_InvalidValue_Fails(NetworkCacheScopeKind kind, string value)
    {
        Assert.False(NetworkCacheScopeParser.TryParse(kind, value, out _));
    }
}
