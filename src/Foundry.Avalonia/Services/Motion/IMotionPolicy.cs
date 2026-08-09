// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Avalonia.Services.Motion;

public interface IMotionPolicy
{
    FoundryMotionMode Mode { get; }

    bool IsAnimationEnabled { get; }
}
