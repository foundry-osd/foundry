// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.NetworkCache;
using Foundry.Core.Services.Telemetry;

namespace Foundry.Core.Tests;

public sealed class NetworkCachePublishTelemetryTests
{
    [Theory]
    [InlineData(new NetworkCacheContentPublishStatus[0], "none")]
    [InlineData(new[] { NetworkCacheContentPublishStatus.Published, NetworkCacheContentPublishStatus.Published }, "published")]
    [InlineData(new[] { NetworkCacheContentPublishStatus.Published, NetworkCacheContentPublishStatus.Unreachable }, "partial")]
    [InlineData(new[] { NetworkCacheContentPublishStatus.Failed, NetworkCacheContentPublishStatus.Unreachable }, "failed")]
    public void Result_SummarizesTheOutcomeOfEveryShare(NetworkCacheContentPublishStatus[] statuses, string expected)
    {
        NetworkCacheContentPublishResult[] results = statuses
            .Select((status, index) => new NetworkCacheContentPublishResult(new NetworkCacheShare { Id = $"s{index}" }, status, null, null))
            .ToArray();

        Assert.Equal(expected, NetworkCachePublishTelemetry.Result(results));
    }

    [Fact]
    public void Result_IsNoneWithoutAPublicationAttempt()
    {
        Assert.Equal("none", NetworkCachePublishTelemetry.Result(null));
    }
}
