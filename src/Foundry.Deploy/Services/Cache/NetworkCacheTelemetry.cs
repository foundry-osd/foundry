// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;

namespace Foundry.Deploy.Services.Cache;

/// <summary>Describes the network cache of a deployment for telemetry, without share paths, servers or accounts.</summary>
public static class NetworkCacheTelemetry
{
    /// <summary>
    /// Returns the session-finished properties. A missing session means the deployment stopped before the cache strategy
    /// step, so whether the cache was enabled is unknown.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> Describe(NetworkCacheSession? session) => new Dictionary<string, object?>
    {
        ["deploy_network_cache_outcome"] = session is null ? "not_resolved" : OutcomeName(session),
        ["deploy_network_cache_writable"] = session?.CanWrite ?? false,
        ["deploy_network_cache_downloads_reused"] = Math.Clamp(session?.DownloadsReused ?? 0, 0, 1000)
    };

    /// <summary>
    /// Returns the session-finished properties of a start that the network cache blocked and the operator closed: no
    /// deployment step ran, and the outcome names the cause without the share, account or address.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> DescribeBlockedStart(NetworkCacheStartupFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new Dictionary<string, object?>
        {
            ["operation_id"] = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            ["deploy_session_success"] = false,
            ["deploy_session_cancelled"] = false,
            ["deploy_session_completed_step_count"] = 0,
            ["deploy_network_cache_outcome"] = OutcomeName(failure.Kind),
            ["deploy_network_cache_writable"] = false,
            ["deploy_network_cache_downloads_reused"] = 0
        };
    }

    /// <summary>Returns the allow-listed <c>deploy_network_cache_outcome</c> value of a startup failure kind.</summary>
    public static string OutcomeName(NetworkCacheStartupFailureKind kind) => kind switch
    {
        NetworkCacheStartupFailureKind.NoScope => "no_scope",
        NetworkCacheStartupFailureKind.Unreachable => "unreachable",
        NetworkCacheStartupFailureKind.AccessDenied => "access_denied",
        NetworkCacheStartupFailureKind.NotPublished => "not_published",
        NetworkCacheStartupFailureKind.ContentMissing => "content_missing",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    // The resolver attaches a failure kind to every scope or share problem, so a session without one is connected or disabled.
    // The coarse "no_matching_scope" and "unavailable" values stay allow-listed for builds that reported them.
    private static string OutcomeName(NetworkCacheSession session) => session.Failure is { } failure
        ? OutcomeName(failure.Kind)
        : session.Outcome == NetworkCacheOutcome.Connected ? "connected" : "disabled";
}
