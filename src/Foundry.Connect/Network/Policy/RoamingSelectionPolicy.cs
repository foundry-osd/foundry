// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Connect.Network.Policy;

/// <summary>
/// What to roam to the deployed Windows at successful exit (D5).
/// </summary>
/// <param name="WifiProfiles">Wi-Fi connections to roam, most preferred first; one entry per profile name.</param>
/// <param name="CaptureWiredDot1x">Whether the wired 802.1X profile is roamed.</param>
public sealed record RoamingSelection(IReadOnlyList<SuccessfulWifiConnection> WifiProfiles, bool CaptureWiredDot1x);

/// <summary>
/// Chooses the network profiles roamed to Windows at successful exit (D5, owner decision 2026-10-29).
/// </summary>
/// <remarks>
/// Every Wi-Fi network that Connect joined successfully during the session is roamed, provisioned or manual, even when
/// Ethernet carries the internet. Order: the current Wi-Fi connection while internet is <see cref="InternetStatus.Online"/>
/// first, then the others by most recent success. A network that was never joined successfully in this session
/// (a failed attempt, or a connection Windows made by itself) is never roamed. Wi-Fi follows the Wi-Fi roaming option;
/// wired 802.1X follows the wired option and needs the wired profile to have been applied.
/// </remarks>
public static class RoamingSelectionPolicy
{
    /// <summary>
    /// Selects the profiles to roam.
    /// </summary>
    /// <param name="state">Network state at exit.</param>
    /// <param name="history">Successful Wi-Fi connections of this session.</param>
    /// <param name="roaming">Roaming options from the configuration.</param>
    /// <param name="wiredDot1xEnabled">Whether the media configures a wired 802.1X profile.</param>
    /// <returns>The selection.</returns>
    public static RoamingSelection Select(
        NetworkState state,
        IReadOnlyList<SuccessfulWifiConnection> history,
        ConnectNetworkProfileRoamingSettings roaming,
        bool wiredDot1xEnabled)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(roaming);

        IReadOnlyList<SuccessfulWifiConnection> wifiProfiles = roaming.Wifi.IsEnabled ? OrderWifi(state, history) : [];
        bool captureWired = roaming.WiredDot1x.IsEnabled && wiredDot1xEnabled && state.Ethernet.Dot1x.IsProfileApplied;
        return new RoamingSelection(wifiProfiles, captureWired);
    }

    private static SuccessfulWifiConnection[] OrderWifi(NetworkState state, IReadOnlyList<SuccessfulWifiConnection> history)
    {
        WifiConnection? carrying = state.Internet == InternetStatus.Online ? state.Wifi.Connection : null;

        // OrderBy is stable: equal times keep the history order (first success first).
        return history
            .OrderByDescending(item => carrying is not null && IsSameConnection(item, carrying))
            .ThenByDescending(static item => item.LastSucceededAt)
            .DistinctBy(static item => item.ProfileName, StringComparer.Ordinal)
            .ToArray();
    }

    // WLAN profiles are per interface; an entry without an SSID (a provisioned profile that states none) matches by
    // interface and profile name only.
    private static bool IsSameConnection(SuccessfulWifiConnection item, WifiConnection connection) =>
        item.InterfaceId == connection.InterfaceId
        && string.Equals(item.ProfileName, connection.ProfileName, StringComparison.Ordinal)
        && (item.Ssid is null || item.Ssid == connection.Ssid);
}
