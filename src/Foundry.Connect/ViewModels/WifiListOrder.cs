// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;

namespace Foundry.Connect.ViewModels;

/// <summary>
/// T12: the list keeps the technician's view stable while a network is selected (L3-9).
/// </summary>
/// <remarks>
/// SSIDs are compared by bytes (<see cref="WifiSsid"/> equality), so names that differ only by case stay separate rows.
/// </remarks>
public static class WifiListOrder
{
    /// <summary>
    /// Computes the order of the list rows after an observation.
    /// </summary>
    /// <param name="current">The SSIDs of the rows shown now, in display order.</param>
    /// <param name="incoming">The visible networks, already in <c>WifiNetworkListPolicy</c> order.</param>
    /// <param name="pinned">The selected network, or <see langword="null"/> when nothing is selected.</param>
    /// <returns>
    /// Without a selection, the incoming order. With a selection, the current rows that are still visible keep their
    /// positions, the selected one stays even when it is no longer visible, and new networks are appended in the
    /// incoming order.
    /// </returns>
    public static IReadOnlyList<WifiSsid> Merge(IReadOnlyList<WifiSsid> current, IReadOnlyList<VisibleWifiNetwork> incoming, WifiSsid? pinned)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(incoming);
        if (pinned is null)
        {
            return incoming.Select(static network => network.Ssid).Distinct().ToArray();
        }

        var visible = new HashSet<WifiSsid>(incoming.Select(static network => network.Ssid));
        var result = new List<WifiSsid>(incoming.Count + 1);
        foreach (WifiSsid ssid in current)
        {
            if ((visible.Contains(ssid) || ssid.Equals(pinned)) && !result.Contains(ssid))
            {
                result.Add(ssid);
            }
        }

        if (!result.Contains(pinned))
        {
            result.Add(pinned);
        }

        foreach (VisibleWifiNetwork network in incoming)
        {
            if (!result.Contains(network.Ssid))
            {
                result.Add(network.Ssid);
            }
        }

        return result;
    }
}
