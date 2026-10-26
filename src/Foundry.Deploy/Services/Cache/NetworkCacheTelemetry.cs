// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Cache;

/// <summary>Describes the network cache of a deployment for telemetry, without share paths, servers or accounts.</summary>
public static class NetworkCacheTelemetry
{
    /// <summary>Returns the session-finished properties; a missing session means the feature was not used.</summary>
    public static IReadOnlyDictionary<string, object?> Describe(NetworkCacheSession? session) => new Dictionary<string, object?>
    {
        ["deploy_network_cache_outcome"] = (session?.Outcome ?? NetworkCacheOutcome.Disabled) switch
        {
            NetworkCacheOutcome.NoMatchingScope => "no_matching_scope",
            NetworkCacheOutcome.Connected => "connected",
            NetworkCacheOutcome.Unavailable => "unavailable",
            _ => "disabled"
        },
        ["deploy_network_cache_writable"] = session?.CanWrite ?? false,
        ["deploy_network_cache_downloads_reused"] = Math.Clamp(session?.DownloadsReused ?? 0, 0, 1000)
    };
}
