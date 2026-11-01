// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Autopilot;
using Microsoft.Extensions.Logging;

namespace Foundry.AutopilotInteractive.Services.Registration;

/// <summary>Outcome of the wait for the Autopilot deployment profile assignment.</summary>
public enum ProfileAssignmentOutcome
{
    /// <summary>A profile is assigned to the device.</summary>
    Assigned,

    /// <summary>Intune reported that the profile assignment failed.</summary>
    Failed,

    /// <summary>No assignment was seen before the stop time.</summary>
    TimedOut,

    /// <summary>The technician skipped the wait.</summary>
    Skipped,

    /// <summary>The status cannot be read: Graph returned an error or does not expose the property.</summary>
    Unavailable
}

/// <summary>
/// Waits for the Autopilot deployment profile assignment of a registered device.
/// </summary>
/// <remarks>Implemented by <see cref="ProfileAssignmentWaiter"/> and, in debug sessions, by a simulation.</remarks>
public interface IProfileAssignmentService
{
    /// <summary>
    /// Waits for the profile assignment of one device. Never throws for Graph failures: they end the wait as
    /// <see cref="ProfileAssignmentOutcome.Unavailable"/>.
    /// </summary>
    /// <param name="accessToken">Graph access token.</param>
    /// <param name="deviceId">Autopilot device identity.</param>
    /// <param name="stopAtUtc">Instant at which the wait gives up.</param>
    /// <param name="skip">Cancelled when the technician skips the wait; wins over every other outcome.</param>
    /// <returns>How the wait ended.</returns>
    Task<ProfileAssignmentOutcome> WaitAsync(string accessToken, string deviceId, DateTimeOffset stopAtUtc, CancellationToken skip);
}

/// <summary>
/// Polls Graph until the registered device has a deployment profile, the wait is skipped, or the stop time is reached.
/// </summary>
/// <remarks>
/// The wait is informational: it never fails the registration. Any failure to read the status ends the wait as
/// <see cref="ProfileAssignmentOutcome.Unavailable"/>, and each read is bounded by the stop time so the deadline margin holds
/// even when the shared client would otherwise retry for minutes. The clock and the delay are injected so the loop can be
/// tested without real time.
/// </remarks>
/// <param name="client">Graph client shared with the registration.</param>
/// <param name="delay">Waits for the given duration; throws <see cref="OperationCanceledException"/> when the token is cancelled.</param>
/// <param name="utcNow">Current UTC instant.</param>
/// <param name="logger">Receives a warning when the status cannot be read; optional.</param>
public sealed class ProfileAssignmentWaiter(
    AutopilotGraphImportClient client,
    Func<TimeSpan, CancellationToken, Task> delay,
    Func<DateTimeOffset> utcNow,
    ILogger<ProfileAssignmentWaiter>? logger = null) : IProfileAssignmentService
{
    /// <summary>Interval between two status reads.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    /// <summary>Longest time the wait lasts, whatever the deadline.</summary>
    public static readonly TimeSpan MaximumWait = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Computes when the wait must stop: <see cref="MaximumWait"/> after the start, or the deadline margin when earlier.
    /// </summary>
    /// <param name="startUtc">Instant the wait starts.</param>
    /// <param name="deadlineUtc">Setup deadline, or <see langword="null"/> when none was passed.</param>
    /// <returns>The stop instant.</returns>
    public static DateTimeOffset ComputeStopAt(DateTimeOffset startUtc, DateTimeOffset? deadlineUtc)
    {
        DateTimeOffset limit = startUtc + MaximumWait;
        DateTimeOffset? deadlineStop = DeadlinePolicy.StopAt(deadlineUtc);
        return deadlineStop is { } stop && stop < limit ? stop : limit;
    }

    /// <inheritdoc />
    /// <remarks>The stop time also bounds a status read still in progress.</remarks>
    public async Task<ProfileAssignmentOutcome> WaitAsync(
        string accessToken,
        string deviceId,
        DateTimeOffset stopAtUtc,
        CancellationToken skip)
    {
        while (true)
        {
            if (skip.IsCancellationRequested)
            {
                return ProfileAssignmentOutcome.Skipped;
            }

            DateTimeOffset now = utcNow();
            if (now >= stopAtUtc)
            {
                return ProfileAssignmentOutcome.TimedOut;
            }

            using CancellationTokenSource readLimit = CancellationTokenSource.CreateLinkedTokenSource(skip);
            readLimit.CancelAfter(stopAtUtc - now);

            string? status;
            try
            {
                status = await client.GetDeploymentProfileAssignmentStatusAsync(accessToken, deviceId, readLimit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (skip.IsCancellationRequested)
            {
                return ProfileAssignmentOutcome.Skipped;
            }
            catch (OperationCanceledException) when (readLimit.IsCancellationRequested)
            {
                return ProfileAssignmentOutcome.TimedOut;
            }
            catch (Exception exception)
            {
                // An HTTP timeout, a Graph error or an unreadable payload: the status is informational only.
                logger?.LogWarning("The deployment profile status could not be read. FailureType={FailureType}", exception.GetType().Name);
                return ProfileAssignmentOutcome.Unavailable;
            }

            if (status is null)
            {
                return ProfileAssignmentOutcome.Unavailable;
            }

            if (status.StartsWith("assigned", StringComparison.OrdinalIgnoreCase))
            {
                return ProfileAssignmentOutcome.Assigned;
            }

            if (status.StartsWith("failed", StringComparison.OrdinalIgnoreCase))
            {
                return ProfileAssignmentOutcome.Failed;
            }

            // The last interval is shortened so the wait ends at the stop time instead of up to one interval later.
            TimeSpan remaining = stopAtUtc - utcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return ProfileAssignmentOutcome.TimedOut;
            }

            try
            {
                await delay(remaining < PollInterval ? remaining : PollInterval, skip).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (skip.IsCancellationRequested)
            {
                return ProfileAssignmentOutcome.Skipped;
            }
        }
    }
}
