// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Contains staged files that remain available after the Windows source image is discarded.
/// </summary>
public sealed record WinPeBootImagePreparationResult
{
    /// <summary>
    /// Gets the dependencies to copy into the mounted boot image's System32 directory.
    /// </summary>
    public required IReadOnlyList<WinPeDependencyFile> DependencyFiles { get; init; }
}

/// <summary>
/// Identifies one staged boot dependency and whether existing destination files should be replaced.
/// </summary>
public sealed record WinPeDependencyFile
{
    /// <summary>
    /// Gets the dependency's System32 file name.
    /// </summary>
    public required string FileName { get; init; }
    /// <summary>
    /// Gets the staged file path outside the Windows source mount.
    /// </summary>
    public required string StagedPath { get; init; }
    /// <summary>
    /// Gets whether the dependency replaces an existing boot image file; graphics dependencies preserve serviced files.
    /// </summary>
    public bool OverwriteExisting { get; init; } = true;
}
