// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Sharing;

namespace Foundry.Core.Tests.Sharing;

public sealed class SyncProgressTests
{
    [Fact]
    public void Percent_NoReportYet_IsZero()
    {
        Assert.Equal(0, SyncProgress.Percent(null));
    }

    [Fact]
    public void Percent_ItemStillHandled_NeverReadsFull()
    {
        Assert.Equal(50, SyncProgress.Percent(new ContentProgress(50, 100, "Drivers2")));
        Assert.Equal(SyncProgress.MaximumWhileRunning, SyncProgress.Percent(new ContentProgress(100, 100, "Drivers2", ContentProgressStep.Installing)));
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(0, 0)]
    public void Percent_FinalReport_IsFull(long done, long total)
    {
        Assert.Equal(100, SyncProgress.Percent(new ContentProgress(done, total, string.Empty)));
    }

    [Fact]
    public void Stage_FollowsTheReportedStep()
    {
        Assert.Equal(SyncStage.Downloading, SyncProgress.Stage(null));
        Assert.Equal(SyncStage.Downloading, SyncProgress.Stage(new ContentProgress(5, 10, "Drivers2")));
        Assert.Equal(SyncStage.Installing, SyncProgress.Stage(new ContentProgress(7, 10, "Drivers2", ContentProgressStep.Installing)));
        Assert.Equal(SyncStage.Complete, SyncProgress.Stage(new ContentProgress(10, 10, string.Empty)));
    }
}
