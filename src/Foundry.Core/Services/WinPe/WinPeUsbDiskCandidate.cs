// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed record WinPeUsbDiskCandidate
{
    public int DiskNumber { get; init; }
    public string FriendlyName { get; init; } = string.Empty;
    public string DriveLetters { get; init; } = string.Empty;
    public string SerialNumber { get; init; } = string.Empty;
    public string UniqueId { get; init; } = string.Empty;
    public string BusType { get; init; } = string.Empty;
    public bool IsSystem { get; init; }
    public bool IsBoot { get; init; }
    public ulong SizeBytes { get; init; }
    public bool IsFoundryMedia { get; init; }

    /// <summary>
    /// Gets the readable volumes found on the disk when the inventory was read. It is empty when the disk
    /// has no partition, when its partitions hold no file system Windows can read, or when reading them failed;
    /// <see cref="PartitionCount"/> and <see cref="VolumesReadFailed"/> tell these cases apart.
    /// </summary>
    public IReadOnlyList<WinPeUsbVolume> Volumes { get; init; } = [];

    /// <summary>Gets the number of partitions the inventory found on the disk.</summary>
    public int PartitionCount { get; init; }

    /// <summary>
    /// Gets whether reading the partitions or volumes of the disk reported an error, or the inventory did not say.
    /// A failed read means the volume list cannot be trusted to be complete.
    /// </summary>
    public bool VolumesReadFailed { get; init; }

    /// <summary>
    /// Summarizes what the inventory knows about the content of the disk. A disk is only called empty when the
    /// read succeeded and found no partition: partitions Windows cannot read (a locked BitLocker drive, a Linux
    /// or macOS disk, an offline disk) may still hold data.
    /// </summary>
    public WinPeUsbVolumeSummary VolumeSummary => Volumes.Count > 0
        ? WinPeUsbVolumeSummary.ReadableVolumes
        : PartitionCount == 0 && !VolumesReadFailed
            ? WinPeUsbVolumeSummary.NoPartition
            : WinPeUsbVolumeSummary.UnreadableVolumes;
}
