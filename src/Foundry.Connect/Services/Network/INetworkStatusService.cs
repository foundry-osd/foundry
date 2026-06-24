// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Models.Network;

namespace Foundry.Connect.Services.Network;

/// <summary>
/// Reads current network adapter, Wi-Fi, and internet status.
/// </summary>
public interface INetworkStatusService
{
    /// <summary>
    /// Gets a point-in-time network status snapshot.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel status probing.</param>
    /// <returns>The current network status snapshot.</returns>
    Task<NetworkStatusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}
