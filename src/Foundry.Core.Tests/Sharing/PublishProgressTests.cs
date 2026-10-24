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
    public void Percent_ContentCopy_FillsTheContentShare()
    {
        Assert.Equal(0, PublishProgress.Percent(new ContentProgress(0, 200, "drivers")));
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
}
