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
    [InlineData(NetworkCacheOutcome.NoMatchingScope, NetworkCacheStartupFailureKind.NoScope, "no_scope")]
    [InlineData(NetworkCacheOutcome.Unavailable, NetworkCacheStartupFailureKind.AccessDenied, "access_denied")]
    [InlineData(NetworkCacheOutcome.Unavailable, NetworkCacheStartupFailureKind.Unreachable, "unreachable")]
    public void Describe_SessionWithFailure_ReportsKindWithoutShareOrAccount(NetworkCacheOutcome outcome, NetworkCacheStartupFailureKind kind, string expected)
    {
        var failure = new NetworkCacheStartupFailure(kind, @"\\fs01\cache", "CONTOSO\\svc", "10.10.4.2");

        IReadOnlyDictionary<string, object?> properties = NetworkCacheTelemetry.Describe(NetworkCacheSession.Unavailable(outcome, failure));

        Assert.Equal(expected, properties["deploy_network_cache_outcome"]);
        Assert.DoesNotContain(properties.Values, value => value is string text && (text.Contains("fs01", StringComparison.OrdinalIgnoreCase) ||
                                                                               text.Contains("svc", StringComparison.OrdinalIgnoreCase) ||
                                                                               text.Contains("10.10", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(NetworkCacheStartupFailureKind.NoScope, "no_scope")]
    [InlineData(NetworkCacheStartupFailureKind.Unreachable, "unreachable")]
    [InlineData(NetworkCacheStartupFailureKind.AccessDenied, "access_denied")]
    [InlineData(NetworkCacheStartupFailureKind.NotPublished, "not_published")]
    [InlineData(NetworkCacheStartupFailureKind.ContentMissing, "content_missing")]
    public void OutcomeName_StartupFailureKind_IsAnAllowedOutcome(NetworkCacheStartupFailureKind kind, string expected)
    {
        string outcome = NetworkCacheTelemetry.OutcomeName(kind);

        Assert.Equal(expected, outcome);
        IReadOnlyDictionary<string, object?> sent = TelemetryEventPropertyPolicy.Sanitize(TelemetryEvents.DeploySessionFinished,
            new Dictionary<string, object?> { ["deploy_network_cache_outcome"] = outcome });
        Assert.Equal(expected, sent["deploy_network_cache_outcome"]);
    }

    [Theory]
    [InlineData(NetworkCacheStartupFailureKind.NoScope, "no_scope")]
    [InlineData(NetworkCacheStartupFailureKind.Unreachable, "unreachable")]
    [InlineData(NetworkCacheStartupFailureKind.AccessDenied, "access_denied")]
    [InlineData(NetworkCacheStartupFailureKind.NotPublished, "not_published")]
    [InlineData(NetworkCacheStartupFailureKind.ContentMissing, "content_missing")]
    public void DescribeBlockedStart_ReportsAFailedSessionWithTheCauseAndNoShareOrAccount(NetworkCacheStartupFailureKind kind, string expected)
    {
        var failure = new NetworkCacheStartupFailure(kind, @"\\fs01\cache", "CONTOSO\\svc", "10.10.4.2");

        IReadOnlyDictionary<string, object?> sent = TelemetryEventPropertyPolicy.Sanitize(TelemetryEvents.DeploySessionFinished,
            NetworkCacheTelemetry.DescribeBlockedStart(failure));

        Assert.Equal(expected, sent["deploy_network_cache_outcome"]);
        Assert.Equal(false, sent["deploy_session_success"]);
        Assert.Equal(false, sent["deploy_session_cancelled"]);
        Assert.Equal(0, sent["deploy_session_completed_step_count"]);
        Assert.Equal(false, sent["deploy_network_cache_writable"]);
        Assert.Equal(0, sent["deploy_network_cache_downloads_reused"]);
        Assert.Matches("^[0-9a-f]{32}$", Assert.IsType<string>(sent["operation_id"]));
        Assert.DoesNotContain(sent.Values, value => value is string text && (text.Contains("fs01", StringComparison.OrdinalIgnoreCase) ||
                                                                         text.Contains("svc", StringComparison.OrdinalIgnoreCase) ||
                                                                         text.Contains("10.10", StringComparison.Ordinal)));
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
