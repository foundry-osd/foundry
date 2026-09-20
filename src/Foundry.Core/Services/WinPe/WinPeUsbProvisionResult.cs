// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed record WinPeUsbProvisionResult
{
    /// <summary>Gets the existing BOOT partition's total size, before formatting; not its current free space.</summary>
    public ulong BootPartitionSizeBytes { get; init; }

    /// <summary>Gets the FAT32 allocation unit size retained when updating the existing BOOT volume.</summary>
    public uint BootAllocationUnitSizeBytes { get; init; }

    public string BootDriveLetter { get; init; } = string.Empty;
    public string CacheDriveLetter { get; init; } = string.Empty;
}
