// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Cache;
using Foundry.Telemetry;
using Foundry.Utilities.Networking;

namespace Foundry.Deploy.Tests;

public sealed class NetworkCacheTelemetryTests
{
    [Fact]
    public void Describe_NoSession_ReportsNotResolved()
    {
        IReadOnlyDictionary<string, object?> properties = NetworkCacheTelemetry.Describe(null);

        Assert.Equal("not_resolved", properties["deploy_network_cache_outcome"]);
        Assert.Equal(false, properties["deploy_network_cache_writable"]);
        Assert.Equal(0, properties["deploy_network_cache_downloads_reused"]);
    }

    [Fact]
    public void Describe_ConnectedSession_ReportsWritableAndReuseCount()
    {
        using NetworkCacheSession session = NetworkCacheSession.Connected(@"\fs01\cache", canWrite: true, new NoopConnector());
        session.RecordReuse();
        session.RecordReuse();

        IReadOnlyDictionary<string, object?> properties = NetworkCacheTelemetry.Describe(session);

        Assert.Equal("connected", properties["deploy_network_cache_outcome"]);
        Assert.Equal(true, properties["deploy_network_cache_writable"]);
        Assert.Equal(2, properties["deploy_network_cache_downloads_reused"]);
        Assert.DoesNotContain(properties.Values, value => value is string text && text.Contains("fs01", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(NetworkCacheOutcome.NoMatchingScope, "no_matching_scope")]
    [InlineData(NetworkCacheOutcome.Unavailable, "unavailable")]
    public void Describe_UnavailableSession_ReportsOutcome(NetworkCacheOutcome outcome, string expected)
    {
        Assert.Equal(expected, NetworkCacheTelemetry.Describe(NetworkCacheSession.Unavailable(outcome))["deploy_network_cache_outcome"]);
    }

    [Fact]
    public void Policy_KeepsNetworkCachePropertiesOnSessionFinished()
    {
        IReadOnlyDictionary<string, object?> sent = TelemetryEventPropertyPolicy.Sanitize(TelemetryEvents.DeploySessionFinished,
            new Dictionary<string, object?>
            {
                ["deploy_network_cache_outcome"] = "connected",
                ["deploy_network_cache_writable"] = true,
                ["deploy_network_cache_downloads_reused"] = 3
            });

        Assert.Equal(3, sent.Count);
    }

    private sealed class NoopConnector : ISmbConnector
    {
        public SmbConnectResult Connect(string uncPath, string? userName, ReadOnlySpan<char> password) => new(SmbConnectStatus.Connected, 0);

        public void Disconnect(string uncPath)
        {
        }
    }
}
