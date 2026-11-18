// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Wpf.Progress;

namespace Foundry.Wpf.Tests.Progress;

public sealed class ProgressPageResourceKeysTests
{
    [Fact]
    public void All_ListsEveryKeyTheViewReadsOnce()
    {
        string[] expected =
        [
            ProgressPageResourceKeys.ProgressSession,
            ProgressPageResourceKeys.ProgressNetworking,
            ProgressPageResourceKeys.ProgressStartTime,
            ProgressPageResourceKeys.ProgressElapsedTime,
            ProgressPageResourceKeys.ProgressTimeline,
            ProgressPageResourceKeys.ProgressTimelineAutomationName,
            ProgressPageResourceKeys.ProgressRingAutomationName,
            ProgressPageResourceKeys.SuccessCompleted,
            ProgressPageResourceKeys.ErrorViewTechnicalDetails,
            ProgressPageResourceKeys.StatusCancellationPending,
            ProgressPageResourceKeys.CommonCancel
        ];

        Assert.Equal(
            new[]
            {
                "Progress.Session", "Progress.Networking", "Progress.StartTime", "Progress.ElapsedTime", "Progress.Timeline",
                "Progress.TimelineAutomationName", "Progress.RingAutomationName", "Success.Completed", "Error.ViewTechnicalDetails",
                "Status.CancellationPending", "Common.Cancel"
            },
            expected);
        Assert.Equal(expected, ProgressPageResourceKeys.All);
        Assert.Equal(11, ProgressPageResourceKeys.All.Distinct(StringComparer.Ordinal).Count());
    }
}
