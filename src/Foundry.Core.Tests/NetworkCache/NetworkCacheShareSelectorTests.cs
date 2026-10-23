// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.NetworkCache;

namespace Foundry.Core.Tests.NetworkCache;

public sealed class NetworkCacheShareSelectorTests
{
    private static readonly DeployNetworkCacheShare ShareOne = new() { Id = "s1", Path = @"\fs01\cache" };
    private static readonly DeployNetworkCacheShare ShareTwo = new() { Id = "s2", Path = @"\fs02\cache" };

    [Fact]
    public void Select_MostSpecificScopeWins()
    {
        DeployNetworkCacheSettings settings = Settings(
            Scope(NetworkCacheScopeKind.Subnet, "0.0.0.0/0", "s1"),
            Scope(NetworkCacheScopeKind.Subnet, "10.10.0.0/16", "s2", "s1"));

        IReadOnlyList<DeployNetworkCacheShare> shares = NetworkCacheShareSelector.Select(settings, [IPAddress.Parse("10.10.1.1")]);

        Assert.Equal(["s2", "s1"], shares.Select(share => share.Id));
    }

    [Fact]
    public void Select_TieKeepsListOrder()
    {
        DeployNetworkCacheSettings settings = Settings(
            Scope(NetworkCacheScopeKind.Subnet, "10.10.0.0/24", "s2"),
            Scope(NetworkCacheScopeKind.Range, "10.10.0.0-10.10.0.255", "s1"));

        IReadOnlyList<DeployNetworkCacheShare> shares = NetworkCacheShareSelector.Select(settings, [IPAddress.Parse("10.10.0.7")]);

        Assert.Equal(["s2"], shares.Select(share => share.Id));
    }

    [Fact]
    public void Select_NoMatch_ReturnsEmpty()
    {
        DeployNetworkCacheSettings settings = Settings(Scope(NetworkCacheScopeKind.Subnet, "10.10.0.0/16", "s1"));

        Assert.Empty(NetworkCacheShareSelector.Select(settings, [IPAddress.Parse("192.168.0.1")]));
    }

    [Fact]
    public void Select_Disabled_ReturnsEmpty()
    {
        DeployNetworkCacheSettings settings = Settings(Scope(NetworkCacheScopeKind.Subnet, "0.0.0.0/0", "s1")) with { IsEnabled = false };

        Assert.Empty(NetworkCacheShareSelector.Select(settings, [IPAddress.Parse("10.0.0.1")]));
    }

    [Fact]
    public void Select_UnknownShareId_IsSkipped()
    {
        DeployNetworkCacheSettings settings = Settings(Scope(NetworkCacheScopeKind.Subnet, "0.0.0.0/0", "missing", "s1"));

        IReadOnlyList<DeployNetworkCacheShare> shares = NetworkCacheShareSelector.Select(settings, [IPAddress.Parse("10.0.0.1")]);

        Assert.Equal(["s1"], shares.Select(share => share.Id));
    }

    [Fact]
    public void Select_IPv6OnlyDevice_MatchesIPv6Scope()
    {
        DeployNetworkCacheSettings settings = Settings(
            Scope(NetworkCacheScopeKind.Subnet, "10.0.0.0/8", "s1"),
            Scope(NetworkCacheScopeKind.Subnet, "fd00:10::/48", "s2"));

        IReadOnlyList<DeployNetworkCacheShare> shares = NetworkCacheShareSelector.Select(settings, [IPAddress.Parse("fd00:10::42")]);

        Assert.Equal(["s2"], shares.Select(share => share.Id));
    }

    [Fact]
    public void Select_InvalidScopeValue_IsIgnored()
    {
        DeployNetworkCacheSettings settings = Settings(
            Scope(NetworkCacheScopeKind.Subnet, "garbage", "s2"),
            Scope(NetworkCacheScopeKind.Subnet, "0.0.0.0/0", "s1"));

        IReadOnlyList<DeployNetworkCacheShare> shares = NetworkCacheShareSelector.Select(settings, [IPAddress.Parse("10.0.0.1")]);

        Assert.Equal(["s1"], shares.Select(share => share.Id));
    }

    private static DeployNetworkCacheSettings Settings(params DeployNetworkCacheScope[] scopes) => new()
    {
        IsEnabled = true,
        Shares = [ShareOne, ShareTwo],
        Scopes = scopes
    };

    private static DeployNetworkCacheScope Scope(NetworkCacheScopeKind kind, string value, params string[] shareIds) => new()
    {
        Kind = kind,
        Value = value,
        ShareIds = shareIds
    };
}
