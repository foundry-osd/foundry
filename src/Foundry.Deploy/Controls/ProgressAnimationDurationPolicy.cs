// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Controls;

public static class ProgressAnimationDurationPolicy
{
    private const double MinimumDurationMilliseconds = 180d;
    private const double MaximumDurationMilliseconds = 450d;

    public static double ClampTarget(double value)
    {
        return Math.Clamp(value, 0d, 100d);
    }

    public static TimeSpan GetDuration(double from, double to)
    {
        double delta = Math.Abs(ClampTarget(to) - ClampTarget(from));
        double ratio = Math.Clamp(delta / 60d, 0d, 1d);
        double milliseconds = MinimumDurationMilliseconds +
                              ((MaximumDurationMilliseconds - MinimumDurationMilliseconds) * ratio);
        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
