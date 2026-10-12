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
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public void VolumeSummary_WithAReadableVolume_ListsVolumesWhateverElseTheDiskHolds(int partitionCount, bool readFailed)
    {
        var candidate = new WinPeUsbDiskCandidate
        {
            PartitionCount = partitionCount,
            VolumesReadFailed = readFailed,
            Volumes = [Volume]
        };

        Assert.Equal(WinPeUsbVolumeSummary.ReadableVolumes, candidate.VolumeSummary);
    }
}
