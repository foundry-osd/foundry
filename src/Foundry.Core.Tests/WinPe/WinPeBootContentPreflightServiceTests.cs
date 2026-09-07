// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeBootContentPreflightServiceTests
{
    private const ulong Mib = 1024 * 1024;
    private const ulong DiskBytes = 64 * 1024 * Mib;

    [Theory]
    [InlineData(uint.MaxValue, true)]
    [InlineData((long)uint.MaxValue + 1, false)]
    [InlineData(-1, false)]
    public void EvaluateSizes_EnforcesFat32SingleFileBoundary(long length, bool accepted)
    {
        var result = WinPeBootContentPreflightService.EvaluateSizes([length], [100], DiskBytes);
        Assert.Equal(accepted, result.IsSuccess);
        if (accepted) { Assert.Equal((ulong)length, result.Value!.LargestBootFileBytes); }
    }

    [Fact]
    public void EvaluateSizes_GrowsBootPartitionAndKeepsCacheReserve()
    {
        var result = WinPeBootContentPreflightService.EvaluateSizes([3L * 1024 * (long)Mib], [123], DiskBytes);
        Assert.True(result.IsSuccess);
        Assert.Equal(3380 * Mib, result.Value!.BootPartitionSizeBytes);
        Assert.Equal(256 * Mib + 123, result.Value.RequiredCacheFreeBytes);
    }

    [Fact]
    public void EvaluateSizes_SmallContentKeepsTwoGiBBaseline()
    {
        var result = WinPeBootContentPreflightService.EvaluateSizes([1], [1], 16 * 1024 * Mib);
        Assert.True(result.IsSuccess);
        Assert.Equal(2048 * Mib, result.Value!.BootPartitionSizeBytes);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("disk")]
    [InlineData("ceiling")]
    [InlineData("runtime-negative")]
    [InlineData("overflow")]
    [InlineData("cache")]
    public void EvaluateSizes_RejectsInvalidOrUnusableCapacity(string failure)
    {
        long[] boot = failure switch { "empty" => [], "ceiling" => Enumerable.Repeat((long)uint.MaxValue, 8).ToArray(), _ => [1] };
        long[] runtime = failure switch
        {
            "runtime-negative" => [-1],
            "overflow" => [long.MaxValue, long.MaxValue, long.MaxValue],
            "cache" => [(long)DiskBytes],
            _ => [1]
        };
        var result = WinPeBootContentPreflightService.EvaluateSizes(boot, runtime, failure == "disk" ? 1 : DiskBytes);
        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData(2048, 512, true)]
    [InlineData(2048, 128, false)]
    [InlineData(512, 512, false)]
    public void EvaluateSizes_UpdatesRequireExistingCapacityWithoutRepartitioning(int bootMib, int cacheMib, bool accepted)
    {
        var result = WinPeBootContentPreflightService.EvaluateSizes([1024L * (long)Mib], [1], DiskBytes,
            (ulong)bootMib * Mib, (ulong)cacheMib * Mib);
        Assert.Equal(accepted, result.IsSuccess);
        if (accepted) { Assert.Equal((ulong)bootMib * Mib, result.Value!.BootPartitionSizeBytes); }
    }

    [Fact]
    public void EvaluateSizes_RejectsUnknownUpdateFreeSpace()
    {
        Assert.False(WinPeBootContentPreflightService.EvaluateSizes([1], [1], DiskBytes, 2048 * Mib).IsSuccess);
    }

    [Fact]
    public void Evaluate_HoldsFinalizedSourceBytesUntilUsbOperationFinishes()
    {
        using var directory = new TemporaryDirectory();
        WinPeBuildArtifact artifact = CreateMedia(directory.Path);
        var result = WinPeBootContentPreflightService.Evaluate(artifact, [10], DiskBytes, null, null, false, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(12UL, result.Value!.BootFileBytes);
        Assert.Throws<IOException>(() => File.WriteAllText(artifact.BootWimPath, "changed"));
        result.Value.Dispose();
        File.WriteAllText(artifact.BootWimPath, "changed");
    }

    [Theory]
    [InlineData("sources/boot.wim")]
    [InlineData("boot/BCD")]
    [InlineData("EFI/Boot/bootx64.efi")]
    public void Evaluate_RejectsMissingRequiredBootFilesBeforeMutation(string missingFile)
    {
        using var directory = new TemporaryDirectory();
        WinPeBuildArtifact artifact = CreateMedia(directory.Path);
        File.Delete(Path.Combine(directory.Path, missingFile));
        var result = WinPeBootContentPreflightService.Evaluate(artifact, [10], DiskBytes, null, null, false, TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData("sources/boot.wim")]
    [InlineData("boot/BCD")]
    [InlineData("EFI/Boot/bootx64.efi")]
    public void Evaluate_RejectsEmptyRequiredBootFilesAndReleasesAllLeases(string emptyFile)
    {
        using var directory = new TemporaryDirectory();
        WinPeBuildArtifact artifact = CreateMedia(directory.Path);
        string emptyPath = Path.Combine(directory.Path, emptyFile);
        File.WriteAllBytes(emptyPath, []);

        var result = WinPeBootContentPreflightService.Evaluate(artifact, [10], DiskBytes, null, null, false, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        foreach (string path in Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories))
        {
            File.WriteAllText(path, "unlocked");
        }
    }

    [Fact]
    public void Evaluate_RejectsEmptyBootExBeforeReplacingStagedEfiFiles()
    {
        using var directory = new TemporaryDirectory();
        WinPeBuildArtifact artifact = CreateMedia(directory.Path);
        string bootExPath = Path.Combine(directory.Path, "bootbins", "bootmgfw_EX.efi");
        Directory.CreateDirectory(Path.GetDirectoryName(bootExPath)!);
        File.WriteAllBytes(bootExPath, []);

        var result = WinPeBootContentPreflightService.Evaluate(artifact, [10], DiskBytes, null, null, true, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("boot", File.ReadAllText(Path.Combine(directory.Path, "EFI", "Boot", "bootx64.efi")));
        File.WriteAllText(bootExPath, "unlocked");
    }

    [Fact]
    public void Evaluate_WhenCapacityFails_ReleasesMeasuredSourceFiles()
    {
        using var directory = new TemporaryDirectory();
        WinPeBuildArtifact artifact = CreateMedia(directory.Path);

        var result = WinPeBootContentPreflightService.Evaluate(artifact, [10], 1, null, null, false, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        File.WriteAllText(artifact.BootWimPath, "unlocked");
    }

    [Fact]
    public void EvaluateSizes_AcceptsExactCapacityAndRejectsOneByteLess()
    {
        const ulong runtimeBytes = 14 * 1024 * Mib - 256 * Mib - 2 * Mib;
        const ulong exactDiskBytes = 16 * 1024 * Mib;

        var exact = WinPeBootContentPreflightService.EvaluateSizes([1], [(long)runtimeBytes], exactDiskBytes);
        var insufficient = WinPeBootContentPreflightService.EvaluateSizes([1], [(long)runtimeBytes + 1], exactDiskBytes);

        Assert.True(exact.IsSuccess);
        Assert.False(insufficient.IsSuccess);
    }

    [Fact]
    public void EvaluateSizes_UpdateRequiresExactCacheReserve()
    {
        var exact = WinPeBootContentPreflightService.EvaluateSizes([1], [100], DiskBytes, 2048 * Mib, 256 * Mib + 100);
        var insufficient = WinPeBootContentPreflightService.EvaluateSizes([1], [100], DiskBytes, 2048 * Mib, 256 * Mib + 99);

        Assert.True(exact.IsSuccess);
        Assert.False(insufficient.IsSuccess);
    }
    private static WinPeBuildArtifact CreateMedia(string directory)
    {
        foreach (string relative in new[] { "sources/boot.wim", "boot/BCD", "EFI/Boot/bootx64.efi" })
        {
            string path = Path.Combine(directory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "boot");
        }
        return new WinPeBuildArtifact
        {
            WorkingDirectoryPath = directory,
            MediaDirectoryPath = directory,
            BootWimPath = Path.Combine(directory, "sources", "boot.wim"),
            Architecture = WinPeArchitecture.X64
        };
    }
}
