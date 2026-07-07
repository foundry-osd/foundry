// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed record WinPeMountedImageCustomizationProgress
{
    public int Percent { get; init; }
    public string Status { get; init; } = string.Empty;
    public int? DetailPercent { get; init; }
    public string DetailStatus { get; init; } = string.Empty;

    /// <summary>
    /// Gets the 1-based index of the current customization task (outer "Task X of N" progress).
    /// </summary>
    public int? TaskIndex { get; init; }

    /// <summary>
    /// Gets the total number of customization tasks for this build.
    /// </summary>
    public int? TaskCount { get; init; }

    /// <summary>
    /// Gets the 1-based index of the current item within a task (inner "item X of N" progress), such as a driver package.
    /// </summary>
    public int? ItemIndex { get; init; }

    /// <summary>
    /// Gets the total number of items within the current task.
    /// </summary>
    public int? ItemCount { get; init; }
}
