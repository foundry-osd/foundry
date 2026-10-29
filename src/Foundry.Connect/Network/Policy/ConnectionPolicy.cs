// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Network.Policy;

/// <summary>
/// Facts the connection policy needs to decide whether to start an automatic provisioned Wi-Fi attempt.
/// </summary>
/// <param name="Mode">Whether automatic attempts are still allowed for this session.</param>
/// <param name="ProvisionedStatus">Readiness of the provisioned profile.</param>
/// <param name="WifiAvailability">Whether a usable wireless adapter exists.</param>
/// <param name="ProvisionedNetworkVisible">Whether the provisioned network is reachable from the scan list; see <see cref="ConnectionPolicy.IsProvisionedNetworkVisible"/>.</param>
/// <param name="ProvisionedProfileIsHidden">Whether the provisioned profile is marked non-broadcast and so never appears in the scan list.</param>
/// <param name="ConnectedToProvisioned">Whether the current Wi-Fi connection already uses the provisioned profile.</param>
/// <param name="Internet">Current internet reachability.</param>
/// <param name="IsOperationRunning">Whether a connect or disconnect operation is in flight.</param>
/// <param name="ConsecutiveFailures">Failed automatic attempts since the last success.</param>
/// <param name="LastAttemptAt">When the last automatic attempt started, or <see langword="null"/> when none ran yet.</param>
/// <param name="Now">The current time, supplied by the caller's <see cref="TimeProvider"/>.</param>
public sealed record AutomaticConnectInput(
    ConnectionMode Mode,
    ProvisionedWifiStatus ProvisionedStatus,
    WifiAvailability WifiAvailability,
    bool ProvisionedNetworkVisible,
    bool ProvisionedProfileIsHidden,
    bool ConnectedToProvisioned,
    InternetStatus Internet,
    bool IsOperationRunning,
    int ConsecutiveFailures,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset Now);

/// <summary>
/// D4: the provisioned Wi-Fi is retried automatically until the technician acts manually.
/// </summary>
public static class ConnectionPolicy
{
    /// <summary>
    /// Delay after the first failed automatic attempt; it doubles with each further consecutive failure.
    /// </summary>
    public static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Upper bound of the delay between automatic attempts.
    /// </summary>
    public static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Any manual connect or disconnect ends automatic mode for the session.
    /// </summary>
    /// <param name="current">The mode before the manual action; it does not change the result.</param>
    /// <returns>Always <see cref="ConnectionMode.Manual"/>.</returns>
    public static ConnectionMode AfterManualAction(ConnectionMode current) => ConnectionMode.Manual;

    /// <summary>
    /// Gets the wait before the next automatic attempt: none before the first attempt or after a success, then 20, 40
    /// and 80 seconds, capped at <see cref="MaximumRetryDelay"/>.
    /// </summary>
    /// <param name="consecutiveFailures">Failed automatic attempts since the last success.</param>
    /// <returns>The minimum interval between two attempts.</returns>
    public static TimeSpan RetryDelay(int consecutiveFailures)
    {
        if (consecutiveFailures <= 0)
        {
            return TimeSpan.Zero;
        }

        double seconds = InitialRetryDelay.TotalSeconds * Math.Pow(2, Math.Min(consecutiveFailures - 1, 10));
        return TimeSpan.FromSeconds(Math.Min(seconds, MaximumRetryDelay.TotalSeconds));
    }

    /// <summary>
    /// Gets whether the provisioned network can be targeted from the current scan list. A profile without an SSID
    /// (an enterprise template that carries none) cannot be matched against the list, so it counts as reachable and
    /// the attempt decides. A non-broadcast profile is handled separately through
    /// <see cref="AutomaticConnectInput.ProvisionedProfileIsHidden"/>.
    /// </summary>
    /// <param name="wifi">The current wireless state.</param>
    /// <returns><see langword="true"/> when the provisioned profile is ready and its SSID is listed or unknown.</returns>
    public static bool IsProvisionedNetworkVisible(WifiState wifi)
    {
        ArgumentNullException.ThrowIfNull(wifi);

        ProvisionedWifiState provisioned = wifi.Provisioned;
        if (provisioned.Status != ProvisionedWifiStatus.Ready)
        {
            return false;
        }

        return provisioned.Ssid is not WifiSsid ssid || wifi.Networks.Any(network => network.Ssid == ssid);
    }

    /// <summary>
    /// Decides whether to start an automatic attempt on the provisioned Wi-Fi now. It requires automatic mode, no
    /// operation in flight, a ready profile, an available adapter, a reachable network, no current provisioned
    /// connection, no internet (a captive portal counts as none, an unfinished check does not) and an elapsed backoff.
    /// </summary>
    /// <param name="input">The facts to decide on.</param>
    /// <returns><see langword="true"/> when an attempt should start.</returns>
    public static bool ShouldConnectProvisionedAutomatically(AutomaticConnectInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.Mode != ConnectionMode.Automatic ||
            input.IsOperationRunning ||
            input.ProvisionedStatus != ProvisionedWifiStatus.Ready ||
            input.WifiAvailability != WifiAvailability.Available ||
            input.ConnectedToProvisioned ||
            !(input.ProvisionedNetworkVisible || input.ProvisionedProfileIsHidden) ||
            input.Internet is not (InternetStatus.NoInternet or InternetStatus.CaptivePortal))
        {
            return false;
        }

        return input.LastAttemptAt is not DateTimeOffset lastAttemptAt ||
               input.Now - lastAttemptAt >= RetryDelay(input.ConsecutiveFailures);
    }
}
