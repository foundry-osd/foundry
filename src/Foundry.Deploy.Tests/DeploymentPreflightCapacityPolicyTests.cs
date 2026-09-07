// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentPreflightCapacityPolicyTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [Fact]
    public void SmallImage_StillRequiresWindowsFloorAndSeparateLayoutPartitions()
    {
        long required = Calculate(20 * GiB);

        Assert.Equal(74377592832L, required);
        Assert.True(required > 64 * GiB);
    }

    [Fact]
    public void LargeImage_RequiresExpandedBytesAndWorkingReserveBeyondWindowsFloor()
    {
        Assert.Equal(119474749440L, Calculate(90 * GiB));
    }

    [Fact]
    public void TargetBackedSource_AddsCompressedImageAndSelectedPayloads()
    {
        long independent = Calculate(90 * GiB, false, 7 * GiB, 3 * GiB);
        long targetBacked = Calculate(90 * GiB, true, 7 * GiB, 3 * GiB);

        Assert.Equal(122695974912L, independent);
        Assert.Equal(130212167680L, targetBacked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownExpandedSize_ReservesSpaceInsteadOfTreatingItAsZero(bool missingImage)
    {
        var selection = new OperatingSystemCatalogItem { SizeBytes = 7 * GiB };
        long required = DeploymentPreflightCapacityPolicy.CalculateRequiredTargetBytes(
            selection, missingImage ? null : Image(0), false);

        Assert.Equal(91557462016L, required);
    }

    [Fact]
    public void UnknownCompressedSize_CannotEstablishTargetBackedCapacity()
    {
        Assert.Throws<InvalidOperationException>(() => Calculate(20 * GiB, true, 0));
        Assert.Equal(74377592832L, Calculate(20 * GiB, false, 0));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(1, -1, 0)]
    [InlineData(1, 0, -1)]
    public void NegativeSizeInputs_AreRejected(long expanded, long compressed, long payload)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculate(expanded, false, compressed, payload));
    }

    [Theory]
    [InlineData(long.MaxValue, 1, 0)]
    [InlineData(1, long.MaxValue, 0)]
    [InlineData(1, 1, long.MaxValue)]
    public void Overflow_CannotWrapIntoARequirementThatAdmitsASmallTarget(long expanded, long compressed, long payload)
    {
        Assert.Throws<OverflowException>(() => Calculate(expanded, true, compressed, payload));
    }

    private static long Calculate(long expanded, bool targetBacked = false, long compressed = 7 * GiB, long payload = 0)
        => DeploymentPreflightCapacityPolicy.CalculateRequiredTargetBytes(
            new OperatingSystemCatalogItem { SizeBytes = compressed }, Image(expanded), targetBacked, payload);

    private static WindowsImageInfo Image(long expanded)
        => new(1, "Professional", "x64", new Version(10, 0, 26100, 1), "en-US", expanded);
}
