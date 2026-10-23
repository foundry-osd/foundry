// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Sizes the single FAT32 partition of a USB drive without cache and checks that the media fits before the disk is erased.
/// </summary>
internal static class WinPeUsbSingleVolumePolicy
{
    /// <summary>Windows formats FAT32 volumes only up to 32 GiB; the rest of a larger drive stays unallocated.</summary>
    internal const ulong MaximumPartitionSizeBytes = 32UL * 1024 * 1024 * 1024;

    /// <summary>The largest file a FAT32 volume can hold.</summary>
    internal const ulong MaximumFileSizeBytes = uint.MaxValue;

    /// <summary>Smallest drive accepted for this layout; the capacity check then decides whether the media fits.</summary>
    internal const ulong MinimumDiskSizeBytes = 1024UL * 1024 * 1024;

    // Covers the partition table, the alignment gap and the Microsoft reserved partition Windows may add on GPT.
    private const ulong PartitionTableReserveBytes = 160UL * 1024 * 1024;
    private const ulong OneMebibyte = 1024UL * 1024;

    /// <summary>Returns the partition size for a drive, aligned down to 1 MiB.</summary>
    internal static ulong GetPartitionSizeBytes(ulong diskSizeBytes)
    {
        ulong usable = diskSizeBytes > PartitionTableReserveBytes ? diskSizeBytes - PartitionTableReserveBytes : 0;
        return Math.Min(usable, MaximumPartitionSizeBytes) / OneMebibyte * OneMebibyte;
    }

    /// <summary>Returns the allocation unit Windows uses by default for a FAT32 volume of this size.</summary>
    internal static uint GetAllocationUnitSizeBytes(ulong partitionSizeBytes) => partitionSizeBytes switch
    {
        <= 8UL * 1024 * 1024 * 1024 => 4096,
        <= 16UL * 1024 * 1024 * 1024 => 8192,
        _ => 16384
    };

    /// <summary>
    /// Rejects a file that FAT32 cannot store, then checks that the media tree and the imported content fit on the partition.
    /// </summary>
    /// <param name="mediaPath">Prepared media tree copied to the root of the partition.</param>
    /// <param name="externalFileLengths">Lengths of the imported files published next to the media tree.</param>
    /// <param name="diskSizeBytes">Captured size of the selected drive.</param>
    internal static WinPeResult Validate(string mediaPath, IEnumerable<long> externalFileLengths, ulong diskSizeBytes,
        CancellationToken cancellationToken)
    {
        ulong partitionSize = GetPartitionSizeBytes(diskSizeBytes);
        uint allocationUnitSize = GetAllocationUnitSizeBytes(partitionSize);
        ulong externalBytes = 0;
        foreach (long length in externalFileLengths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (length < 0) return WinPeUsbCapacityPolicy.UnknownCapacity();
            if ((ulong)length > MaximumFileSizeBytes)
            {
                return WinPeResult.Failure(new WinPeDiagnostic(WinPeErrorCodes.UsbFileTooLargeForFat32,
                    "A file of the media is larger than a FAT32 volume can hold.")
                {
                    RequiredBytes = (ulong)length,
                    AvailableBytes = MaximumFileSizeBytes
                });
            }

            externalBytes = checked(externalBytes + RoundUp((ulong)length, allocationUnitSize));
        }

        // Manifests and directory entries of the imported content.
        externalBytes = checked(externalBytes + (ulong)WinPeCustomImageMediaService.DataReserveBytes);
        if (externalBytes >= partitionSize)
        {
            return CapacityFailure(externalBytes, partitionSize);
        }

        WinPeResult mediaResult = WinPeUsbCapacityPolicy.Validate(mediaPath, partitionSize - externalBytes, allocationUnitSize, cancellationToken);
        if (mediaResult.Error is { Code: WinPeErrorCodes.UsbBootCapacityInsufficient, RequiredBytes: ulong mediaBytes })
        {
            return CapacityFailure(checked(mediaBytes + externalBytes), partitionSize);
        }

        return mediaResult;
    }

    private static WinPeResult CapacityFailure(ulong required, ulong available) =>
        WinPeResult.Failure(new WinPeDiagnostic(WinPeErrorCodes.UsbBootCapacityInsufficient,
            "The media does not fit on the USB drive.")
        {
            RequiredBytes = required,
            AvailableBytes = available
        });

    private static ulong RoundUp(ulong bytes, uint allocationUnitSize) =>
        checked((bytes + allocationUnitSize - 1) / allocationUnitSize * allocationUnitSize);
}
