// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeScaledProgressTests
{
    [Theory]
    [InlineData(0, 55)]
    [InlineData(50, 65)]
    [InlineData(100, 75)]
    [InlineData(150, 75)]
    public void Report_MapsChildRangeIntoParentRange(int childPercent, int expectedPercent)
    {
        var parent = new CollectingProgress();
        var scaled = new WinPeScaledProgress(parent, 55, 75);

        scaled.Report(new WinPeMediaProgress { Percent = childPercent, Status = "Copying." });

        WinPeMediaProgress report = Assert.Single(parent.Reports);
        Assert.Equal(expectedPercent, report.Percent);
        Assert.Equal("Copying.", report.Status);
    }

    [Fact]
    public void Create_WithoutParent_ReturnsNull()
    {
        Assert.Null(WinPeScaledProgress.Create(null, 55, 75));
    }

    private sealed class CollectingProgress : IProgress<WinPeMediaProgress>
    {
        public List<WinPeMediaProgress> Reports { get; } = [];

        public void Report(WinPeMediaProgress value)
        {
            Reports.Add(value);
        }
    }
}
