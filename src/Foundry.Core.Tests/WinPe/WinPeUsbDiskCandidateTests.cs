// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeUsbDiskCandidateTests
{
    private static readonly WinPeUsbVolume Volume = new() { DriveLetter = "E:", FileSystem = "NTFS" };

    [Theory]
    [InlineData(0, false, WinPeUsbVolumeSummary.NoPartition)]
    [InlineData(3, false, WinPeUsbVolumeSummary.UnreadableVolumes)]
    [InlineData(0, true, WinPeUsbVolumeSummary.UnreadableVolumes)]
    [InlineData(2, true, WinPeUsbVolumeSummary.UnreadableVolumes)]
    public void VolumeSummary_WithoutReadableVolume_SaysNoPartitionOnlyWhenTheReadSucceededAndFoundNone(
        int partitionCount, bool readFailed, WinPeUsbVolumeSummary expected)
    {
        var candidate = new WinPeUsbDiskCandidate { PartitionCount = partitionCount, VolumesReadFailed = readFailed };

        Assert.Equal(expected, candidate.VolumeSummary);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void VolumeSummary_WhenEveryOtherPartitionIsReadable_ListsTheVolumes(int partitionCount)
    {
        var candidate = new WinPeUsbDiskCandidate { PartitionCount = partitionCount, Volumes = [Volume] };

        Assert.Equal(WinPeUsbVolumeSummary.ReadableVolumes, candidate.VolumeSummary);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void VolumeSummary_WithAReadableVolumeAndUnreadableContent_WarnsThatTheListIsIncomplete(bool readFailed, int unreadablePartitionCount)
    {
        // A small readable volume next to an APFS, Linux or locked BitLocker partition must not make the disk look empty.
        var candidate = new WinPeUsbDiskCandidate
        {
            PartitionCount = 3,
            UnreadablePartitionCount = unreadablePartitionCount,
            VolumesReadFailed = readFailed,
            Volumes = [Volume]
        };

        Assert.Equal(WinPeUsbVolumeSummary.ReadableAndUnreadableVolumes, candidate.VolumeSummary);
    }
}
