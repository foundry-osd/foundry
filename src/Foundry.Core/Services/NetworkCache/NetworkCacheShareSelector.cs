// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using Foundry.Core.Models.Configuration.Deploy;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>Chooses the shares a device tries from its addresses and the authored scopes.</summary>
public static class NetworkCacheShareSelector
{
    /// <summary>
    /// Returns the shares of the most specific scope that contains any local address, in that scope's order.
    /// Ties keep authoring order; invalid scopes and unknown share ids are skipped; disabled settings return none.
    /// </summary>
    public static IReadOnlyList<DeployNetworkCacheShare> Select(DeployNetworkCacheSettings settings, IEnumerable<IPAddress> localAddresses)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(localAddresses);
        if (!settings.IsEnabled) return [];
        IPAddress[] addresses = [.. localAddresses];
        DeployNetworkCacheScope? best = null;
        UInt128 bestSize = UInt128.MaxValue;
        foreach (DeployNetworkCacheScope scope in settings.Scopes)
        {
            if (!NetworkCacheScopeParser.TryParse(scope.Kind, scope.Value, out ParsedNetworkCacheScope? parsed) ||
                !addresses.Any(parsed.Contains) ||
                (best is not null && parsed.Size >= bestSize))
            {
                continue;
            }

            best = scope;
            bestSize = parsed.Size;
        }

        if (best is null) return [];
        return [.. best.ShareIds
            .Select(id => settings.Shares.FirstOrDefault(share => string.Equals(share.Id, id, StringComparison.OrdinalIgnoreCase)))
            .OfType<DeployNetworkCacheShare>()];
    }
}
