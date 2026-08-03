// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes how Foundry.Connect continues the bootstrap after Internet connectivity is validated.
/// </summary>
public sealed record ConnectAutoContinueSettings
{
    /// <summary>
    /// Gets the delay applied when the generated configuration does not specify one.
    /// </summary>
    public const int DefaultDelaySeconds = 10;

    /// <summary>
    /// Gets the smallest supported delay; zero continues as soon as connectivity is validated.
    /// </summary>
    public const int MinimumDelaySeconds = 0;

    /// <summary>
    /// Gets the largest supported delay.
    /// </summary>
    public const int MaximumDelaySeconds = 300;

    /// <summary>
    /// Gets whether Foundry.Connect continues automatically once it has Internet access.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// Gets the countdown shown before Foundry.Connect continues automatically.
    /// </summary>
    public int DelaySeconds { get; init; } = DefaultDelaySeconds;

    /// <summary>
    /// Constrains a delay to the supported range.
    /// </summary>
    /// <param name="delaySeconds">The delay to constrain.</param>
    /// <returns>The delay clamped to the supported range.</returns>
    public static int ClampDelaySeconds(int delaySeconds) =>
        Math.Clamp(delaySeconds, MinimumDelaySeconds, MaximumDelaySeconds);
}
