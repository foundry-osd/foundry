// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeUsbSingleVolumePolicyTests
{
    [Fact]
    public void GetPartitionSizeBytes_OnALargeDrive_CapsAtTheFat32FormatLimit()
    {
        Assert.Equal(32UL * 1024 * 1024 * 1024, WinPeUsbSingleVolumePolicy.GetPartitionSizeBytes(64_000_000_000UL));
    }

    [Fact]
    public void GetPartitionSizeBytes_OnASmallDrive_KeepsRoomForThePartitionTableAndAlignsToOneMebibyte()
    {
        // 16,000,000,000 bytes minus the 160 MiB reserve, rounded down to 1 MiB.
        Assert.Equal(15_098UL * 1024 * 1024, WinPeUsbSingleVolumePolicy.GetPartitionSizeBytes(16_000_000_000UL));
    }

    [Fact]
    public void Validate_WhenAFileIsFourGibibytes_ReturnsFat32FileFailure()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "sources"));
        File.WriteAllText(Path.Combine(temp.Path, "sources", "boot.wim"), "wim");

        WinPeResult result = WinPeUsbSingleVolumePolicy.Validate(temp.Path, [4L * 1024 * 1024 * 1024], 64_000_000_000UL,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.UsbFileTooLargeForFat32, result.Error!.Code);
        Assert.Equal(4UL * 1024 * 1024 * 1024, result.Error.RequiredBytes);
        Assert.Equal((ulong)uint.MaxValue, result.Error.AvailableBytes);
    }

    [Fact]
    public void Validate_WhenTheLargestFileFitsFat32_Succeeds()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "sources"));
        File.WriteAllText(Path.Combine(temp.Path, "sources", "boot.wim"), "wim");

        WinPeResult result = WinPeUsbSingleVolumePolicy.Validate(temp.Path, [uint.MaxValue], 64_000_000_000UL,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Error?.Message);
    }

    [Fact]
    public void Validate_WhenContentExceedsTheDrive_ReturnsCapacityFailure()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "sources"));
        File.WriteAllText(Path.Combine(temp.Path, "sources", "boot.wim"), "wim");

        WinPeResult result = WinPeUsbSingleVolumePolicy.Validate(temp.Path, [3_000_000_000L, 3_000_000_000L], 4_000_000_000UL,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(WinPeErrorCodes.UsbBootCapacityInsufficient, result.Error!.Code);
        Assert.True(result.Error.RequiredBytes > result.Error.AvailableBytes);
        Assert.Equal(WinPeUsbSingleVolumePolicy.GetPartitionSizeBytes(4_000_000_000UL), result.Error.AvailableBytes);
    }
}
