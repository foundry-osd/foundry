// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Describes a volume Windows can read on a USB disk, so the user can recognize the disk before it is erased.
/// Partitions without a file system are not represented.
/// </summary>
public sealed record WinPeUsbVolume
{
    /// <summary>Gets the drive letter with its colon (for example <c>E:</c>), or an empty string when none is assigned.</summary>
    public string DriveLetter { get; init; } = string.Empty;

    /// <summary>Gets the file system label, or an empty string when the volume has no name.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Gets the file system name reported by Windows, for example <c>NTFS</c> or <c>exFAT</c>.</summary>
    public string FileSystem { get; init; } = string.Empty;

    /// <summary>Gets the total volume size in bytes.</summary>
    public ulong SizeBytes { get; init; }

    /// <summary>Gets the used space in bytes: the volume size minus the remaining free space.</summary>
    public ulong UsedBytes { get; init; }
}
