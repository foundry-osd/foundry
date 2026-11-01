// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.AutopilotInteractive.Services.Registration;

/// <summary>
/// Rules for the deadline Foundry.PostInstall passes with <c>--deadline</c>, after which Windows setup continues
/// without registration.
/// </summary>
public static class DeadlinePolicy
{
    /// <summary>Margin kept before the deadline so the result is written and the process exits before PostInstall stops it.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Computes the instant at which long waits must stop.
    /// </summary>
    /// <param name="deadlineUtc">The deadline, or <see langword="null"/> when none was passed.</param>
    /// <returns>The deadline minus <see cref="Margin"/>, or <see langword="null"/> without a deadline.</returns>
    public static DateTimeOffset? StopAt(DateTimeOffset? deadlineUtc) => deadlineUtc - Margin;

    /// <summary>
    /// Tells whether the deadline itself has been reached.
    /// </summary>
    /// <param name="deadlineUtc">The deadline, or <see langword="null"/> when none was passed.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns><see langword="true"/> when a deadline exists and <paramref name="nowUtc"/> is at or after it.</returns>
    public static bool IsReached(DateTimeOffset? deadlineUtc, DateTimeOffset nowUtc) =>
        deadlineUtc is not null && nowUtc >= deadlineUtc.Value;
}
