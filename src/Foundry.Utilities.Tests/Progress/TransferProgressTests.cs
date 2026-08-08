// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Progress;

namespace Foundry.Utilities.Tests.Progress;

public sealed class TransferProgressTests
{
    [Theory]
    [InlineData(0L, null)]
    [InlineData(0L, 0L)]
    [InlineData(10L, -1L)]
    public void CalculatePercentage_WithUnknownOrInvalidTotal_ReturnsNull(long transferred, long? total)
    {
        Assert.Null(TransferProgress.CalculatePercentage(transferred, total));
    }

    [Theory]
    [InlineData(-1, 100, 0d)]
    [InlineData(0, 100, 0d)]
    [InlineData(50, 100, 50d)]
    [InlineData(100, 100, 100d)]
    [InlineData(150, 100, 100d)]
    [InlineData(long.MaxValue / 2, long.MaxValue, 50d)]
    public void CalculatePercentage_WithKnownTotal_ReturnsClampedRatio(long transferred, long total, double expected)
    {
        double? percentage = TransferProgress.CalculatePercentage(transferred, total);

        Assert.Equal(expected, percentage);
    }
}
