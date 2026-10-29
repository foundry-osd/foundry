// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network.Native;

namespace Foundry.Connect.Network.Policy;

/// <summary>
/// Builds the list of Wi-Fi networks shown to the technician (D6, NET-12).
/// </summary>
public static class WifiNetworkListPolicy
{
    /// <summary>
    /// Builds the visible network list.
    /// </summary>
    /// <remarks>
    /// Hidden SSIDs (empty or all-zero) are excluded. Networks are identified by SSID bytes, so names that differ
    /// only by case stay separate. Duplicates seen on several interfaces or access points are merged keeping the
    /// strongest signal (the first entry wins on a tie). The list is ordered by signal descending, then display name
    /// (<see cref="StringComparison.OrdinalIgnoreCase"/>), then SSID hex (<see cref="StringComparison.Ordinal"/>).
    /// </remarks>
    /// <param name="networks">Raw available networks of every interface.</param>
    /// <returns>The ordered visible networks.</returns>
    public static IReadOnlyList<VisibleWifiNetwork> Build(IEnumerable<WlanAvailableNetworkInfo> networks)
    {
        ArgumentNullException.ThrowIfNull(networks);

        Dictionary<WifiSsid, VisibleWifiNetwork> strongest = [];
        foreach (WlanAvailableNetworkInfo network in networks)
        {
            if (IsHidden(network.SsidBytes) || !WifiSsid.TryFromBytes(network.SsidBytes, out WifiSsid? ssid))
            {
                continue;
            }

            int signal = Math.Clamp(network.SignalQuality, 0, 100);
            if (strongest.TryGetValue(ssid, out VisibleWifiNetwork? existing) && existing.SignalQuality >= signal)
            {
                continue;
            }

            strongest[ssid] = new VisibleWifiNetwork(
                network.InterfaceId,
                ssid,
                WifiSecurityClassifier.Classify(network.AuthAlgorithm, network.SecurityEnabled),
                signal);
        }

        return strongest.Values
            .OrderByDescending(static item => item.SignalQuality)
            .ThenBy(static item => item.Ssid.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Ssid.Hex, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsHidden(byte[] ssidBytes)
    {
        return ssidBytes.Length == 0 || !ssidBytes.AsSpan().ContainsAnyExcept((byte)0);
    }
}
