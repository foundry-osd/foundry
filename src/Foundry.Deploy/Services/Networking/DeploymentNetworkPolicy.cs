// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;

namespace Foundry.Deploy.Services.Networking;

/// <summary>Fixes network eligibility for the lifetime of a deployment process.</summary>
public sealed record DeploymentNetworkPolicy(bool OfflineOnly)
{
    /// <summary>Rejects network fallback when verified local content is required.</summary>
    public void ThrowIfNetworkUnavailable()
    {
        if (OfflineOnly)
            throw new InvalidOperationException("This offline deployment requires verified local content. Reconnect and restart in online mode to acquire missing content.");
    }
}
