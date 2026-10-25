// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Sharing;

namespace Foundry.Core.Tests.Sharing;

public sealed class PublishProgressTests
{
    [Fact]
    public void Percent_NoReportYet_IsZero()
    {
        Assert.Equal(0, PublishProgress.Percent(null));
    }

    [Fact]
    public void Percent_CheckingTheFolder_IsZero()
    {
        Assert.Equal(0, PublishProgress.Percent(new ContentProgress(0, 0, string.Empty, ContentProgressStep.CheckingFolder)));
    }

    [Fact]
    public void Percent_ContentSteps_FillTheContentShare()
    {
        Assert.Equal(0, PublishProgress.Percent(new ContentProgress(0, 200, "drivers", ContentProgressStep.Preparing)));
        Assert.Equal(PublishProgress.ContentShare / 2, PublishProgress.Percent(new ContentProgress(100, 200, "drivers")));
    }

    [Theory]
    [InlineData(200, 200)]
    [InlineData(0, 0)]
    public void Percent_FinalContentReport_LeavesTheManifestStep(long done, long total)
    {
        double percent = PublishProgress.Percent(new ContentProgress(done, total, string.Empty));

        Assert.Equal(PublishProgress.ContentShare, percent);
        Assert.True(percent < 100);
    }

    [Fact]
    public void Stage_FollowsTheReportedStep()
    {
        Assert.Equal(PublishStage.CheckingFolder, PublishProgress.Stage(null));
        Assert.Equal(PublishStage.CheckingFolder, PublishProgress.Stage(new ContentProgress(0, 0, string.Empty, ContentProgressStep.CheckingFolder)));
        Assert.Equal(PublishStage.Verifying, PublishProgress.Stage(new ContentProgress(0, 10, "Win11.wim", ContentProgressStep.Verifying)));
        Assert.Equal(PublishStage.Preparing, PublishProgress.Stage(new ContentProgress(0, 10, "Drivers2", ContentProgressStep.Preparing)));
        Assert.Equal(PublishStage.Sending, PublishProgress.Stage(new ContentProgress(5, 10, "Win11.wim")));
        Assert.Equal(PublishStage.Finishing, PublishProgress.Stage(new ContentProgress(10, 10, string.Empty)));
    }
}
