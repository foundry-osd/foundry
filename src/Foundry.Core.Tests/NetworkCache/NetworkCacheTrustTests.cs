// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.NetworkCache;
using Foundry.Core.Services.Sharing;

namespace Foundry.Core.Tests.NetworkCache;

public sealed class NetworkCacheTrustTests
{
    private static readonly byte[] OwnKey = [1, 2, 3];
    private static readonly byte[] PinnedKey = [4, 5, 6];

    [Theory]
    [InlineData(SharingRole.None)]
    [InlineData(SharingRole.Reference)]
    public void Resolve_PublishingPc_TrustsItsOwnKeyAndPublishes(SharingRole role)
    {
        (byte[] PublicKey, bool CanPublishIndex)? trust = NetworkCacheTrust.Resolve(role, OwnKey, PinnedKey);

        Assert.NotNull(trust);
        Assert.Equal(OwnKey, trust.Value.PublicKey);
        Assert.True(trust.Value.CanPublishIndex);
    }

    [Fact]
    public void Resolve_PublishingPcWithoutKey_TrustsNothing()
    {
        Assert.Null(NetworkCacheTrust.Resolve(SharingRole.Reference, null, PinnedKey));
    }

    [Fact]
    public void Resolve_FollowerWithPinnedKey_TrustsThePinnedKeyAndNeverPublishes()
    {
        (byte[] PublicKey, bool CanPublishIndex)? trust = NetworkCacheTrust.Resolve(SharingRole.Follower, OwnKey, PinnedKey);

        Assert.NotNull(trust);
        Assert.Equal(PinnedKey, trust.Value.PublicKey);
        Assert.False(trust.Value.CanPublishIndex);
    }

    [Fact]
    public void Resolve_FollowerWithoutPinnedKey_TrustsNothing()
    {
        Assert.Null(NetworkCacheTrust.Resolve(SharingRole.Follower, OwnKey, null));
    }

    [Fact]
    public void Resolve_PublishingPcWithEmptyKey_TrustsNothing()
    {
        Assert.Null(NetworkCacheTrust.Resolve(SharingRole.None, [], PinnedKey));
    }

    [Fact]
    public void Resolve_FollowerWithEmptyPinnedKey_TrustsNothing()
    {
        Assert.Null(NetworkCacheTrust.Resolve(SharingRole.Follower, OwnKey, []));
    }

    [Theory]
    [InlineData(SharingRole.None, true)]
    [InlineData(SharingRole.Reference, true)]
    [InlineData(SharingRole.Follower, false)]
    [InlineData((SharingRole)42, false)]
    public void CanPublish_OnlyAReferenceOrStandalonePcPublishes(SharingRole role, bool expected)
    {
        Assert.Equal(expected, NetworkCacheTrust.CanPublish(role));
    }

    [Theory]
    [InlineData(SharingRole.None, true)]
    [InlineData(SharingRole.Reference, false)]
    [InlineData(SharingRole.Follower, false)]
    [InlineData((SharingRole)42, false)]
    public void CanCreateSigningKey_OnlyAStandalonePcCreatesItsKey(SharingRole role, bool expected)
    {
        Assert.Equal(expected, NetworkCacheTrust.CanCreateSigningKey(role));
    }
}
