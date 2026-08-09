// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Networking;

/// <summary>
/// Provides raw network adapter snapshots.
/// </summary>
public interface INetworkAdapterSnapshotProvider
{
    /// <summary>
    /// Gets the current network adapters in platform enumeration order.
    /// </summary>
    IReadOnlyList<NetworkAdapterSnapshot> GetAdapters();
}
