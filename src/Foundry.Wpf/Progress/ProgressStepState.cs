// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Wpf.Progress;

/// <summary>Display state of a progress page timeline step; hosts map their own step states to it by name.</summary>
public enum ProgressStepState
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Skipped,
    Cancelled
}
