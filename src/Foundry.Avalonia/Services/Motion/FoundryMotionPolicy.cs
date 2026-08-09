// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Avalonia.Services.Motion;

public sealed class FoundryMotionPolicy : IMotionPolicy
{
    public FoundryMotionPolicy(
        bool isWinPe,
        bool isOperatingSystemAnimationEnabled,
        FoundryMotionMode? overrideMode = null)
    {
        Mode = overrideMode ?? SelectDefaultMode(isWinPe, isOperatingSystemAnimationEnabled);
    }

    public FoundryMotionMode Mode { get; }

    public bool IsAnimationEnabled => Mode != FoundryMotionMode.None;

    private static FoundryMotionMode SelectDefaultMode(
        bool isWinPe,
        bool isOperatingSystemAnimationEnabled)
    {
        if (!isOperatingSystemAnimationEnabled)
        {
            return FoundryMotionMode.None;
        }

        return isWinPe ? FoundryMotionMode.Reduced : FoundryMotionMode.Full;
    }
}
