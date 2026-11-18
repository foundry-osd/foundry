// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Wpf.Progress;

/// <summary>
/// Resource keys <see cref="ProgressPageView"/> reads through <see cref="IProgressPageViewModel.Strings"/>. Every host that
/// shows the page must define all of them in its own resources.
/// </summary>
public static class ProgressPageResourceKeys
{
    public const string ProgressSession = "Progress.Session";
    public const string ProgressNetworking = "Progress.Networking";
    public const string ProgressStartTime = "Progress.StartTime";
    public const string ProgressElapsedTime = "Progress.ElapsedTime";
    public const string ProgressTimeline = "Progress.Timeline";
    public const string ProgressTimelineAutomationName = "Progress.TimelineAutomationName";
    public const string ProgressRingAutomationName = "Progress.RingAutomationName";
    public const string SuccessCompleted = "Success.Completed";
    public const string ErrorViewTechnicalDetails = "Error.ViewTechnicalDetails";
    public const string StatusCancellationPending = "Status.CancellationPending";
    public const string CommonCancel = "Common.Cancel";

    /// <summary>Gets every key the page reads, once.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        ProgressSession,
        ProgressNetworking,
        ProgressStartTime,
        ProgressElapsedTime,
        ProgressTimeline,
        ProgressTimelineAutomationName,
        ProgressRingAutomationName,
        SuccessCompleted,
        ErrorViewTechnicalDetails,
        StatusCancellationPending,
        CommonCancel
    ];
}
