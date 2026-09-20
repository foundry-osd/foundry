// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>Budgets the prepared FAT32 payload before any destructive USB operation.</summary>
internal static class WinPeUsbCapacityPolicy
{
    internal const ulong NewBootPartitionSizeBytes = 2048UL * 1024 * 1024;
    internal const uint NewBootAllocationUnitSizeBytes = 4096;

    /// <summary>Includes cluster rounding, long-name directory entries and a filesystem reserve.</summary>
    internal static WinPeResult Validate(string mediaPath, ulong capacity, uint allocationUnitSize, CancellationToken cancellationToken)
    {
        if (capacity == 0 || allocationUnitSize is < 512 or > 262144 ||
            (allocationUnitSize & (allocationUnitSize - 1)) != 0)
        {
            return UnknownCapacity();
        }

        try
        {
            // Two FATs need less than 1.6% even with 512-byte clusters. Reserve 2%, at least
            // 16 MiB, for FATs, reserved sectors and formatting overhead, independent of old files.
            ulong required = Math.Max(16UL * 1024 * 1024, capacity / 50);
            var directories = new Stack<DirectoryInfo>();
            directories.Push(new DirectoryInfo(mediaPath));
            while (directories.TryPop(out DirectoryInfo? directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return UnknownCapacity();
                }

                ulong directoryBytes = 3 * 32; // Dot entries plus a volume-label/end entry allowance.
                foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        return UnknownCapacity();
                    }

                    // A long FAT name occupies 13 UTF-16 characters per entry, plus its short name.
                    directoryBytes = checked(directoryBytes + (ulong)(1 + ((entry.Name.Length + 12) / 13)) * 32);
                    if (entry is DirectoryInfo child)
                    {
                        directories.Push(child);
                    }
                    else if (entry is FileInfo file)
                    {
                        ulong length = checked((ulong)file.Length);
                        if (length > uint.MaxValue)
                        {
                            return SizeFailure(WinPeErrorCodes.UsbBootFileTooLarge,
                                "A prepared BOOT file exceeds the FAT32 file size limit.", length, uint.MaxValue);
                        }
                        required = checked(required + RoundUp(length, allocationUnitSize));
                    }
                }
                required = checked(required + RoundUp(directoryBytes, allocationUnitSize));
            }

            return required <= capacity ? WinPeResult.Success() : SizeFailure(
                WinPeErrorCodes.UsbBootCapacityInsufficient,
                "The prepared media does not fit on the USB BOOT partition.", required, capacity);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OverflowException or ArgumentException)
        {
            return UnknownCapacity();
        }
    }

    /// <summary>Fails closed when the prepared payload or target geometry cannot be measured.</summary>
    internal static WinPeResult UnknownCapacity() => WinPeResult.Failure(new WinPeDiagnostic(
        WinPeErrorCodes.UsbBootCapacityUnknown,
        "USB BOOT capacity could not be verified. No formatting was started."));

    private static ulong RoundUp(ulong bytes, uint allocationUnitSize) =>
        checked(((bytes + allocationUnitSize - 1) / allocationUnitSize) * allocationUnitSize);

    private static WinPeResult SizeFailure(string code, string message, ulong required, ulong available) =>
        WinPeResult.Failure(new WinPeDiagnostic(code, message)
        {
            RequiredBytes = required,
            AvailableBytes = available
        });
}
