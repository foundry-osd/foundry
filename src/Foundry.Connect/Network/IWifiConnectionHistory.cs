// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Network;

/// <summary>
/// Read-only session history of successful Wi-Fi connections, kept by the connection coordinator.
/// </summary>
public interface IWifiConnectionHistory
{
    /// <summary>
    /// Gets a snapshot of the successful connections, one entry per interface and profile name, in order of first
    /// success. A repeated success updates the entry's time. A provisioned profile that Windows joined by itself is
    /// recorded when an observation first shows it connected. The history is never logged.
    /// </summary>
    IReadOnlyList<SuccessfulWifiConnection> SuccessfulWifiConnections { get; }
}
