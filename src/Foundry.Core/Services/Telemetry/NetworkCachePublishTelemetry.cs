// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.NetworkCache;
using Foundry.Telemetry;

namespace Foundry.Core.Services.Telemetry;

/// <summary>Maps the share results of a network cache publication to the <c>network_cache_publish_result</c> value.</summary>
public static class NetworkCachePublishTelemetry
{
    /// <summary>
    /// Returns <see cref="TelemetryNetworkCachePublishResults.None"/> without results, <see cref="TelemetryNetworkCachePublishResults.Published"/>
    /// when every share was published, <see cref="TelemetryNetworkCachePublishResults.Failed"/> when none was, and
    /// <see cref="TelemetryNetworkCachePublishResults.Partial"/> otherwise.
    /// </summary>
    /// <param name="results">One result per share, or <see langword="null"/> when no publication was attempted.</param>
    public static string Result(IReadOnlyList<NetworkCacheContentPublishResult>? results)
    {
        if (results is null || results.Count == 0) return TelemetryNetworkCachePublishResults.None;
        int published = results.Count(result => result.Status == NetworkCacheContentPublishStatus.Published);
        return published == results.Count ? TelemetryNetworkCachePublishResults.Published
            : published == 0 ? TelemetryNetworkCachePublishResults.Failed
            : TelemetryNetworkCachePublishResults.Partial;
    }
}
