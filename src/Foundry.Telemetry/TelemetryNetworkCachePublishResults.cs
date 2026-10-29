// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Telemetry;

/// <summary>
/// Defines the stable values of <c>network_cache_publish_result</c>: how a publication to the network cache shares ended.
/// </summary>
public static class TelemetryNetworkCachePublishResults
{
    /// <summary>Every share received the content and the index.</summary>
    public const string Published = "published";

    /// <summary>Some shares received them and others did not.</summary>
    public const string Partial = "partial";

    /// <summary>No share received them.</summary>
    public const string Failed = "failed";

    /// <summary>No publication was attempted: the network cache is off, or the operation stopped before publishing.</summary>
    public const string None = "none";
}
