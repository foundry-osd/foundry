// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Defines and normalizes the supported post-deployment reboot delay.
/// </summary>
public static class DeploymentRebootDelay
{
    public const int DefaultSeconds = 10;

    public const int MaximumSeconds = 3600;

    public static int NormalizeAuthoring(double seconds)
    {
        if (!double.IsFinite(seconds))
        {
            return DefaultSeconds;
        }

        return (int)Math.Clamp(Math.Ceiling(seconds), 0, MaximumSeconds);
    }

    public static int NormalizeRuntime(int seconds)
    {
        return seconds is >= 0 and <= MaximumSeconds ? seconds : DefaultSeconds;
    }
}
