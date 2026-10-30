// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Wpf.Controls;

/// <summary>
/// Computes how long a progress indicator animates between two percentage values, so small steps stay
/// snappy and large jumps stay readable. Shared by the progress controls of every WPF application.
/// </summary>
public static class ProgressAnimationDurationPolicy
{
    private const double MinimumDurationMilliseconds = 180d;
    private const double MaximumDurationMilliseconds = 450d;

    /// <summary>Clamps a progress value to the 0-100 percentage range.</summary>
    public static double ClampTarget(double value)
    {
        return Math.Clamp(value, 0d, 100d);
    }

    /// <summary>
    /// Returns a duration scaled linearly from 180 ms to 450 ms with the distance between the two values;
    /// both values are clamped to 0-100 first and a distance of 60 points or more yields the maximum.
    /// </summary>
    public static TimeSpan GetDuration(double from, double to)
    {
        double delta = Math.Abs(ClampTarget(to) - ClampTarget(from));
        double ratio = Math.Clamp(delta / 60d, 0d, 1d);
        double milliseconds = MinimumDurationMilliseconds +
                              ((MaximumDurationMilliseconds - MinimumDurationMilliseconds) * ratio);
        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
