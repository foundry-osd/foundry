// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Network.Policy;

/// <summary>
/// How the network gate ends the startup of Foundry Connect.
/// </summary>
public enum NetworkGateOutcome
{
    /// <summary>The internet was online: Connect completed through the successful exit without showing its page.</summary>
    SkipConnect,

    /// <summary>The internet was not confirmed in time, or skipping is not allowed: the Connect page is shown.</summary>
    ShowConnect
}

/// <summary>
/// The network gate rule: after the startup provisioning the internet is probed every <see cref="RecheckInterval"/> for at
/// most <see cref="Duration"/>; an <see cref="InternetStatus.Online"/> probe skips Connect at once, a captive portal, no
/// internet or the deadline shows it. Like the automatic continue, a skip never happens while a deployment password
/// prompt is open. Pure decisions: the caller owns the probe, the clock and the prompt state.
/// </summary>
public static class NetworkGatePolicy
{
    /// <summary>
    /// Longest time the gate probes before it shows Connect.
    /// </summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Time between the starts of two probes.
    /// </summary>
    public static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Decides after a probe: skip when it is online (within the gate and with no password prompt open), show Connect once
    /// the deadline is reached, else probe again.
    /// </summary>
    /// <param name="internet">The result of the probe.</param>
    /// <param name="elapsed">Time since the first probe started.</param>
    /// <param name="isPasswordPromptOpen">Whether a deployment password prompt is open.</param>
    /// <returns>The outcome, or <see langword="null"/> to probe again.</returns>
    public static NetworkGateOutcome? Evaluate(InternetStatus internet, TimeSpan elapsed, bool isPasswordPromptOpen)
    {
        if (internet == InternetStatus.Online && !isPasswordPromptOpen && elapsed <= Duration)
        {
            return NetworkGateOutcome.SkipConnect;
        }

        return elapsed >= Duration ? NetworkGateOutcome.ShowConnect : null;
    }

    /// <summary>
    /// Gets the wait before the next probe: <see cref="RecheckInterval"/> after the start of the last one, never past
    /// <see cref="Duration"/>, and none when that time has already passed.
    /// </summary>
    /// <param name="probeStartedAt">When the last probe started, since the first probe started.</param>
    /// <param name="now">The current time, since the first probe started.</param>
    /// <returns>The wait.</returns>
    public static TimeSpan NextProbeDelay(TimeSpan probeStartedAt, TimeSpan now)
    {
        TimeSpan next = probeStartedAt + RecheckInterval;
        TimeSpan due = next < Duration ? next : Duration;
        return due > now ? due - now : TimeSpan.Zero;
    }
}
