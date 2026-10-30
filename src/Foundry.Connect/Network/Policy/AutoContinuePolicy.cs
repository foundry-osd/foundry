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
/// <param name="SecondsRemaining">Whole seconds left; above zero only while <see cref="AutoContinueStatus.Counting"/>.</param>
public sealed record AutoContinueState(AutoContinueStatus Status, int SecondsRemaining)
{
    /// <summary>
    /// Gets the state before the internet is first online.
    /// </summary>
    public static AutoContinueState Initial { get; } = new(AutoContinueStatus.Idle, 0);
}

/// <summary>
/// A 10 second countdown starts once the internet is online and no deployment password prompt is open; an interaction
/// pauses it for the session (or keeps it from starting when it happened before), "Stay here" cancels it, and losing the
/// internet or opening the prompt resets it. Pure transitions: the caller owns the one second timer, the session
/// interaction flag and the prompt flag.
/// </summary>
/// <remarks>
/// Like the Foundry Deploy reboot countdown, each tick removes one whole second and the countdown ends on the tick
/// that reaches zero, so every second from 10 down to 1 shows exactly once whatever the timer jitter.
/// </remarks>
public static class AutoContinuePolicy
{
    /// <summary>
    /// Length of the countdown, in whole seconds (one tick each).
    /// </summary>
    public const int CountdownSeconds = FoundryConnectApplicationInfo.DefaultAutoContinueDelaySeconds;

    /// <summary>
    /// Starts the countdown when the internet comes online and resets a running one when it is lost (a captive portal
    /// counts as lost). After a networking interaction earlier in the session the internet coming online offers Continue
    /// without counting: the state goes straight to <see cref="AutoContinueStatus.Paused"/>. While the deployment password
    /// prompt is open the internet counts as not online yet: nothing starts and a running countdown is reset, so the
    /// caller applies the status again once the prompt closes. Paused, cancelled and elapsed states are left as they are.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <param name="internet">The latest internet status.</param>
    /// <param name="hasNetworkingInteraction">Whether the technician interacted with networking during the session.</param>
    /// <param name="isPasswordPromptOpen">Whether the deployment password prompt is open.</param>
    /// <returns>The next state.</returns>
    public static AutoContinueState OnInternetStatus(AutoContinueState state, InternetStatus internet, bool hasNetworkingInteraction, bool isPasswordPromptOpen)
    {
        bool isOnline = internet == InternetStatus.Online && !isPasswordPromptOpen;
        return state.Status switch
        {
            AutoContinueStatus.Idle when isOnline && hasNetworkingInteraction => new(AutoContinueStatus.Paused, 0),
            AutoContinueStatus.Idle when isOnline => new(AutoContinueStatus.Counting, CountdownSeconds),
            AutoContinueStatus.Counting when !isOnline => AutoContinueState.Initial,
            _ => state
        };
    }

    /// <summary>
    /// Pauses a running countdown after a networking interaction; any other state is unchanged.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <returns>The next state.</returns>
    public static AutoContinueState OnNetworkingInteraction(AutoContinueState state) =>
        state.Status == AutoContinueStatus.Counting ? new(AutoContinueStatus.Paused, 0) : state;

    /// <summary>
    /// Cancels the countdown for the session; an elapsed countdown stays elapsed.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <returns>The next state.</returns>
    public static AutoContinueState OnStayHere(AutoContinueState state) =>
        state.Status == AutoContinueStatus.Elapsed ? state : new(AutoContinueStatus.Cancelled, 0);

    /// <summary>
    /// Removes one second from a running countdown and ends it when none is left; any other state is unchanged.
    /// </summary>
    /// <param name="state">The current state.</param>
    /// <returns>The next state.</returns>
    public static AutoContinueState OnTick(AutoContinueState state) => state.Status switch
    {
        AutoContinueStatus.Counting when state.SecondsRemaining > 1 => state with { SecondsRemaining = state.SecondsRemaining - 1 },
        AutoContinueStatus.Counting => new(AutoContinueStatus.Elapsed, 0),
        _ => state
    };

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
