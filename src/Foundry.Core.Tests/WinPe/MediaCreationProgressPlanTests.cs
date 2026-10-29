// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class MediaCreationProgressPlanTests
{
    [Fact]
    public void Report_ComputesWeightedPercentAndStep()
    {
        MediaCreationProgressPlan plan = MediaCreationProgressPlan.For(MediaCreationKind.Iso);

        MediaCreationProgress progress = plan.Report(MediaCreationPhase.CustomizingBootImage, 50);

        Assert.Equal(MediaCreationPhase.CustomizingBootImage, progress.Phase);
        Assert.Equal(44, progress.Percent);
        Assert.Equal(3, progress.StepNumber);
        Assert.Equal(5, progress.StepCount);
    }

    [Theory]
    [InlineData(MediaCreationKind.Iso, 6)]
    [InlineData(MediaCreationKind.UsbCreate, 7)]
    [InlineData(MediaCreationKind.UsbUpdate, 6)]
    [InlineData(MediaCreationKind.Pxe, 6)]
    public void For_WithNetworkCachePublication_AddsAStepBeforeFinishing(MediaCreationKind kind, int stepCount)
    {
        MediaCreationProgressPlan plan = MediaCreationProgressPlan.For(kind, publishesToNetworkCache: true);

        MediaCreationProgress publishing = plan.Report(MediaCreationPhase.PublishingToNetworkCache, 100);
        MediaCreationProgress finishing = plan.Report(MediaCreationPhase.Finishing, 100);

        Assert.True(plan.Includes(MediaCreationPhase.PublishingToNetworkCache));
        Assert.Equal(stepCount - 1, publishing.StepNumber);
        Assert.Equal(stepCount, publishing.StepCount);
        Assert.Equal(95, publishing.Percent);
        Assert.Equal(100, finishing.Percent);
    }

    [Fact]
    public void For_WithoutNetworkCachePublication_KeepsTheMediaSteps()
    {
        MediaCreationProgressPlan plan = MediaCreationProgressPlan.For(MediaCreationKind.Iso);

        Assert.False(plan.Includes(MediaCreationPhase.PublishingToNetworkCache));
        Assert.Equal(5, plan.Current.StepCount);
    }

    [Fact]
    public void ReportFinalMedia_ForPxe_MapsToCreatingPxeFiles()
    {
        MediaCreationProgressPlan plan = MediaCreationProgressPlan.For(MediaCreationKind.Pxe);

        MediaCreationProgress progress = plan.ReportFinalMedia(50);

        Assert.Equal(MediaCreationPhase.CreatingPxeFiles, progress.Phase);
        Assert.Equal(4, progress.StepNumber);
        Assert.Equal(5, progress.StepCount);
        Assert.Equal(90, progress.Percent);
    }

    [Fact]
    public void Report_NeverDecreases()
    {
        MediaCreationProgressPlan plan = MediaCreationProgressPlan.For(MediaCreationKind.UsbCreate);
        MediaCreationProgress later = plan.Report(MediaCreationPhase.CopyingToUsbDrive, 50);

        MediaCreationProgress earlier = plan.Report(MediaCreationPhase.FormattingUsbDrive, 10);

        Assert.Equal(later.Percent, earlier.Percent);
        Assert.Equal(MediaCreationPhase.CopyingToUsbDrive, earlier.Phase);
        Assert.Equal(later.StepNumber, earlier.StepNumber);
    }

    [Fact]
    public void Report_SamePhaseLowerPercent_KeepsPercent()
    {
        MediaCreationProgressPlan plan = MediaCreationProgressPlan.For(MediaCreationKind.Iso);
        MediaCreationProgress first = plan.Report(MediaCreationPhase.CreatingIso, 80);

        MediaCreationProgress second = plan.Report(MediaCreationPhase.CreatingIso, 10);

        Assert.Equal(first.Percent, second.Percent);
    }

    [Theory]
    [InlineData(54, MediaCreationPhase.FormattingUsbDrive)]
    [InlineData(55, MediaCreationPhase.CopyingToUsbDrive)]
    public void ReportFinalMedia_UsbCreate_SplitsAtFormattingBoundary(int finalMediaPercent, MediaCreationPhase expectedPhase)
    {
        MediaCreationProgressPlan plan = MediaCreationProgressPlan.For(MediaCreationKind.UsbCreate);

        MediaCreationProgress progress = plan.ReportFinalMedia(finalMediaPercent);

        Assert.Equal(expectedPhase, progress.Phase);
    }

    [Fact]
    public void ReportFinalMedia_UsbCreate_StartsCopyPhaseAtItsBeginning()
    {
        MediaCreationProgressPlan plan = MediaCreationProgressPlan.For(MediaCreationKind.UsbCreate);

        MediaCreationProgress progress = plan.ReportFinalMedia(MediaCreationProgressPlan.UsbFormattingCompletePercent);

        Assert.Equal(8 + 10 + 42 + 10, progress.Percent);
    }

    [Theory]
    [InlineData(MediaCreationKind.Iso, MediaCreationPhase.CreatingIso)]
    [InlineData(MediaCreationKind.UsbUpdate, MediaCreationPhase.UpdatingUsbDrive)]
    public void ReportFinalMedia_SingleOutputPhase_UsesTargetPhase(MediaCreationKind kind, MediaCreationPhase expectedPhase)
    {
        MediaCreationProgressPlan plan = MediaCreationProgressPlan.For(kind);

        MediaCreationProgress progress = plan.ReportFinalMedia(40);

        Assert.Equal(expectedPhase, progress.Phase);
    }

    [Theory]
    [InlineData(-5, 8)]
    [InlineData(150, 18)]
    public void Report_ClampsPhasePercent(int phasePercent, int expectedPercent)
    {
        MediaCreationProgressPlan plan = MediaCreationProgressPlan.For(MediaCreationKind.Iso);
        plan.Report(MediaCreationPhase.Preparing, 100);

        MediaCreationProgress progress = plan.Report(MediaCreationPhase.DownloadingDrivers, phasePercent);

        Assert.Equal(expectedPercent, progress.Percent);
    }

    [Theory]
    [InlineData(MediaCreationKind.Iso)]
    [InlineData(MediaCreationKind.UsbCreate)]
    [InlineData(MediaCreationKind.UsbUpdate)]
    public void Report_FinishingComplete_Returns100(MediaCreationKind kind)
    {
        MediaCreationProgressPlan plan = MediaCreationProgressPlan.For(kind);

        MediaCreationProgress progress = plan.Report(MediaCreationPhase.Finishing, 100);

        Assert.Equal(100, progress.Percent);
        Assert.Equal(progress.StepCount, progress.StepNumber);
    }

    [Fact]
    public void Report_PhaseNotInPlan_Throws()
    {
        MediaCreationProgressPlan plan = MediaCreationProgressPlan.For(MediaCreationKind.Iso);

        Assert.Throws<ArgumentOutOfRangeException>(() => plan.Report(MediaCreationPhase.FormattingUsbDrive, 10));
    }
}
