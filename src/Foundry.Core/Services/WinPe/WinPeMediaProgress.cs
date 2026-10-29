// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed record WinPeMediaProgress
{
    public int Percent { get; init; }
    public string Status { get; init; } = string.Empty;
    public string LogDetail { get; init; } = string.Empty;

    /// <summary>Gets the content bytes processed so far by a step that copies or checks files, or <see langword="null"/>.</summary>
    public long? BytesCompleted { get; init; }

    /// <summary>Gets the content bytes the step processes in total, or <see langword="null"/>.</summary>
    public long? TotalBytes { get; init; }
}
