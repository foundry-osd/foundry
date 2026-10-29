// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Network.Policy;

/// <summary>
/// Phase of the automatic continue countdown.
/// </summary>
public enum AutoContinueStatus
{
    /// <summary>Waiting for the internet to come online.</summary>
    Idle,

    /// <summary>The countdown is running.</summary>
    Counting,

    /// <summary>The technician touched networking this session; the countdown stays off and Continue is offered.</summary>
    Paused,

    /// <summary>The technician chose to stay; it stays off for the session.</summary>
    Cancelled,

    /// <summary>The countdown finished and Connect may continue.</summary>
    Elapsed
}

/// <summary>
/// Immutable automatic continue state.
/// </summary>
/// <param name="Status">Current phase.</param>
/// <param name="Deadline">When the countdown ends; set only while <see cref="AutoContinueStatus.Counting"/>.</param>
public sealed record AutoContinueState(AutoContinueStatus Status, DateTimeOffset? Deadline)
{
    /// <summary>
    /// Gets the state before the internet is first online.
    /// </summary>
    public static AutoContinueState Initial { get; } = new(AutoContinueStatus.Idle, null);

    /// <summary>
    /// Gets the time left on the running countdown.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <returns>The remaining time, or <see cref="TimeSpan.Zero"/> when no countdown runs or it has ended.</returns>
    public TimeSpan RemainingAt(DateTimeOffset now) =>
        Status == AutoContinueStatus.Counting && Deadline is DateTimeOffset deadline && deadline > now ? deadline - now : TimeSpan.Zero;
}

/// <summary>
/// D3: a 10 second countdown starts once the internet is online; an interaction pauses it for the session (or keeps it
/// from starting when it happened before), "Stay here" cancels it, and losing the internet resets it. Pure transitions:
/// the caller owns the clock, the timer and the session interaction flag.
/// </summary>
public static class AutoContinuePolicy
{
    /// <summary>
    /// Length of the countdown.
    /// </summary>
    public static readonly TimeSpan Countdown = TimeSpan.FromSeconds(FoundryConnectApplicationInfo.DefaultAutoContinueDelaySeconds);

    /// <summary>
    /// Starts the countdown when the internet comes online and resets a running one when it is lost (a captive portal
    /// counts as lost). After a networking interaction earlier in the session the internet coming online offers Continue
    /// without counting: the state goes straight to <see cref="AutoContinueStatus.Paused"/>. Paused, cancelled and
    /// elapsed states are left as they are.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <param name="internet">The latest internet status.</param>
    /// <param name="now">The current time.</param>
    /// <param name="hasNetworkingInteraction">Whether the technician interacted with networking during the session.</param>
    /// <returns>The next state.</returns>
    public static AutoContinueState OnInternetStatus(AutoContinueState state, InternetStatus internet, DateTimeOffset now, bool hasNetworkingInteraction) => state.Status switch
    {
        AutoContinueStatus.Idle when internet == InternetStatus.Online && hasNetworkingInteraction => new(AutoContinueStatus.Paused, null),
        AutoContinueStatus.Idle when internet == InternetStatus.Online => new(AutoContinueStatus.Counting, now + Countdown),
        AutoContinueStatus.Counting when internet != InternetStatus.Online => AutoContinueState.Initial,
        _ => state
    };

    /// <summary>
    /// Pauses a running countdown after a networking interaction; any other state is unchanged.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <returns>The next state.</returns>
    public static AutoContinueState OnNetworkingInteraction(AutoContinueState state) =>
        state.Status == AutoContinueStatus.Counting ? new(AutoContinueStatus.Paused, null) : state;

    /// <summary>
    /// Cancels the countdown for the session; an elapsed countdown stays elapsed.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <returns>The next state.</returns>
    public static AutoContinueState OnStayHere(AutoContinueState state) =>
        state.Status == AutoContinueStatus.Elapsed ? state : new(AutoContinueStatus.Cancelled, null);

    /// <summary>
    /// Ends a running countdown whose deadline has passed.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The next state.</returns>
    public static AutoContinueState OnTick(AutoContinueState state, DateTimeOffset now) =>
        state.Status == AutoContinueStatus.Counting && state.Deadline is DateTimeOffset deadline && now >= deadline
            ? new(AutoContinueStatus.Elapsed, null)
            : state;

    /// <summary>
    /// The final probe before exit failed: wait for the next online state.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <returns>The next state.</returns>
    public static AutoContinueState OnFinalProbeFailed(AutoContinueState state) =>
        state.Status == AutoContinueStatus.Elapsed ? AutoContinueState.Initial : state;

    /// <summary>
    /// Gets whether Connect should continue now.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <returns><see langword="true"/> once the countdown has elapsed.</returns>
    public static bool ShouldContinue(AutoContinueState state) => state.Status == AutoContinueStatus.Elapsed;
}
