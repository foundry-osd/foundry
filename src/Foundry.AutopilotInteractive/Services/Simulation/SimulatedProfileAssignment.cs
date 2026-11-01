// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.AutopilotInteractive.Services.Registration;

namespace Foundry.AutopilotInteractive.Services.Simulation;

/// <summary>
/// <see cref="IProfileAssignmentService"/> for debug scenarios: ends the wait after a fixed time, never calling Graph.
/// </summary>
/// <remarks>Like the real waiter, the wait ends at the stop time and a skip wins over every other outcome.</remarks>
/// <param name="scenario">Scenario that decides the outcome.</param>
/// <param name="delay">Waits for the given duration; throws <see cref="OperationCanceledException"/> when the token is cancelled.</param>
/// <param name="utcNow">Current UTC instant.</param>
public sealed class SimulatedProfileAssignment(
    DebugScenario scenario,
    Func<TimeSpan, CancellationToken, Task> delay,
    Func<DateTimeOffset> utcNow) : IProfileAssignmentService
{
    /// <summary>Simulated time before the profile is assigned or the short wait times out.</summary>
    public static readonly TimeSpan WaitDuration = TimeSpan.FromSeconds(8);

    /// <inheritdoc />
    public async Task<ProfileAssignmentOutcome> WaitAsync(string accessToken, string deviceId, DateTimeOffset stopAtUtc, CancellationToken skip)
    {
        (TimeSpan duration, ProfileAssignmentOutcome outcome) = scenario switch
        {
            DebugScenario.ProfileWait => (Timeout.InfiniteTimeSpan, ProfileAssignmentOutcome.TimedOut),
            DebugScenario.ProfileTimeout => (WaitDuration, ProfileAssignmentOutcome.TimedOut),
            _ => (WaitDuration, ProfileAssignmentOutcome.Assigned)
        };

        TimeSpan untilStop = stopAtUtc - utcNow();
        if (untilStop <= TimeSpan.Zero)
        {
            return ProfileAssignmentOutcome.TimedOut;
        }

        bool stopsFirst = duration == Timeout.InfiniteTimeSpan || untilStop <= duration;
        try
        {
            await delay(stopsFirst ? untilStop : duration, skip).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (skip.IsCancellationRequested)
        {
            return ProfileAssignmentOutcome.Skipped;
        }

        return stopsFirst ? ProfileAssignmentOutcome.TimedOut : outcome;
    }
}
