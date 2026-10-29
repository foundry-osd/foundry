// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Security;

/// <summary>
/// Delay applied after a wrong deployment password before the prompt reopens, shared by Foundry Connect and Foundry Deploy.
/// </summary>
public static class DeploymentPasswordRetryPolicy
{
    private const int MaximumDelaySeconds = 5;

    /// <summary>
    /// Gets the progressive delay for a failed attempt: one second per failed attempt, capped at five seconds.
    /// </summary>
    /// <param name="failedAttemptNumber">One-based number of the failed attempt.</param>
    /// <returns>The delay to wait before the next attempt.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="failedAttemptNumber"/> is zero or negative.</exception>
    public static TimeSpan GetDelay(int failedAttemptNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(failedAttemptNumber);
        return TimeSpan.FromSeconds(Math.Min(failedAttemptNumber, MaximumDelaySeconds));
    }
}
