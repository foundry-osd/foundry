// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentTimelineTrackerTests
{
    [Fact]
    public void Reset_StartsFreshOutcomesAndUsesSelectedPlanLabels()
    {
        var tracker = CreateTracker();

        tracker.Reset([new("Prepare", "Old label")]);
        tracker.Apply(CreateProgress(1, DeploymentStepState.Succeeded));

        tracker.Reset([new("Prepare", "Check setup"), new("Apply", "Apply image"), new("Finalize", "Finish deployment")]);

        Assert.Equal(3, tracker.Entries.Count);
        Assert.Equal("Check setup", tracker.Entries[0].DisplayName);
        Assert.All(tracker.Entries, entry => Assert.Empty(entry.DetailText));
        Assert.All(tracker.Entries, entry => Assert.Equal(DeploymentStepState.Pending, entry.State));
    }

    [Fact]
    public void Apply_UpdatesOnlyTheReportedStepIdentity()
    {
        var tracker = CreateTracker();
        tracker.Reset([new("Prepare", "Prepare"), new("Apply", "Apply")]);

        tracker.Apply(CreateProgress(stepIndex: 2, state: DeploymentStepState.Running));

        Assert.Equal(DeploymentStepState.Pending, tracker.Entries[0].State);
        Assert.Equal(DeploymentStepState.Running, tracker.Entries[1].State);
    }

    [Fact]
    public void FailAt_PreservesCompletedEntriesAndMarksReportedOperationFailed()
    {
        var tracker = CreateTracker();
        tracker.Reset([new("Prepare", "Prepare"), new("Apply", "Apply"), new("Finalize", "Finalize")]);
        tracker.Apply(CreateProgress(1, DeploymentStepState.Succeeded));

        tracker.FailAt(2);

        Assert.Equal(DeploymentStepState.Succeeded, tracker.Entries[0].State);
        Assert.Equal(DeploymentStepState.Failed, tracker.Entries[1].State);
        Assert.Equal(DeploymentStepState.Pending, tracker.Entries[2].State);
    }

    [Fact]
    public void FailAt_AfterOperationCompleted_PreservesItsRecordedOutcome()
    {
        var tracker = CreateTracker();
        tracker.Reset([new("Prepare", "Prepare")]);
        tracker.Apply(CreateProgress(1, DeploymentStepState.Succeeded));

        tracker.FailAt(1);

        Assert.Equal(DeploymentStepState.Succeeded, tracker.Entries[0].State);
    }

    [Fact]
    public void Reconcile_DoesNotFabricateResultsForUnreportedOperations()
    {
        var tracker = CreateTracker();
        tracker.Reset([new("Prepare", "Prepare")]);

        tracker.Reconcile([new("Prepare", "Prepare"), new("Apply", "Apply")]);

        Assert.All(tracker.Entries, entry => Assert.Equal(DeploymentStepState.Pending, entry.State));
    }

    [Fact]
    public void Reconcile_PreservesSkippedResultsWhenFutureWorkIsRemoved()
    {
        var tracker = CreateTracker();
        tracker.Reset([new("Prepare", "Prepare"), new("Apply", "Apply")]);
        tracker.Apply(CreateProgress(1, DeploymentStepState.Skipped));

        tracker.Reconcile([new("Prepare", "Prepare")]);

        Assert.Equal(DeploymentStepState.Skipped, tracker.Entries[0].State);
        Assert.Single(tracker.Entries);

    }

    [Fact]
    public void Apply_AfterPlanReordering_UsesStableIdentityAndRetainsReason()
    {
        var tracker = CreateTracker();
        tracker.Reset([new("Prepare", "Prepare"), new("Download", "Download"), new("Apply", "Apply")]);
        tracker.Apply(new DeploymentStepProgress
        {
            StepName = "Download",
            State = DeploymentStepState.Skipped,
            StepIndex = 1,
            StepCount = 2,
            ProgressPercent = 50,
            Message = "Cached image reused.",
            Plan = [new("Download", "Download Image"), new("Apply", "Apply Image")]
        });

        Assert.Equal("Download", tracker.Entries[0].RawName);
        Assert.Equal(DeploymentStepState.Skipped, tracker.Entries[0].State);
        Assert.Equal("Cached image reused.", tracker.Entries[0].DetailText);
        Assert.Equal(DeploymentStepState.Pending, tracker.Entries[1].State);
    }

    [Fact]
    public void Apply_ExposesLocalizedStateTextForAutomation()
    {
        var tracker = CreateTracker();
        tracker.Reset([new("Prepare", "Prepare")]);

        tracker.Apply(CreateProgress(1, DeploymentStepState.Running));

        Assert.Equal("Localized Running", tracker.Entries[0].StateAutomationText);
    }

    private static DeploymentTimelineTracker CreateTracker()
    {
        return new DeploymentTimelineTracker(name => name, state => $"Localized {state}");
    }

    private static DeploymentStepProgress CreateProgress(int stepIndex, DeploymentStepState state)
    {
        return new DeploymentStepProgress
        {
            StepName = stepIndex == 1 ? "Prepare" : "Apply",
            State = state,
            StepIndex = stepIndex,
            StepCount = 2,
            ProgressPercent = 50
        };
    }
}
