// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed record WinPeDismProgress
{
    public int Percent { get; init; }

    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional 1-based index of the current item within a multi-item stage (for example the
    /// optional component being applied).
    /// </summary>
    public int? ItemIndex { get; init; }

    /// <summary>
    /// Gets the optional total number of items within a multi-item stage.
    /// </summary>
    public int? ItemCount { get; init; }
}
