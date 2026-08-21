// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentTimelineTrackerTests
{
    [Fact]
    public void Reset_CreatesOnePendingEntryPerPlannedOperation()
    {
        var tracker = CreateTracker();

        tracker.Reset(["Prepare", "Apply", "Finalize"]);

        Assert.Equal(3, tracker.Entries.Count);
        Assert.All(tracker.Entries, entry => Assert.Equal(DeploymentStepState.Pending, entry.State));
    }

    [Fact]
    public void Apply_UpdatesOnlyTheReportedOneBasedStep()
    {
        var tracker = CreateTracker();
        tracker.Reset(["Prepare", "Apply"]);

        tracker.Apply(CreateProgress(stepIndex: 2, state: DeploymentStepState.Running));

        Assert.Equal(DeploymentStepState.Pending, tracker.Entries[0].State);
        Assert.Equal(DeploymentStepState.Running, tracker.Entries[1].State);
    }

    [Fact]
    public void FailAt_PreservesCompletedEntriesAndMarksReportedOperationFailed()
    {
        var tracker = CreateTracker();
        tracker.Reset(["Prepare", "Apply", "Finalize"]);
        tracker.Apply(CreateProgress(1, DeploymentStepState.Succeeded));

        tracker.FailAt(2);

        Assert.Equal(DeploymentStepState.Succeeded, tracker.Entries[0].State);
        Assert.Equal(DeploymentStepState.Failed, tracker.Entries[1].State);
        Assert.Equal(DeploymentStepState.Pending, tracker.Entries[2].State);
    }

    [Fact]
    public void CompleteAll_UsesCompletedSemanticsForEveryOperation()
    {
        var tracker = CreateTracker();
        tracker.Reset(["Prepare", "Apply"]);

        tracker.CompleteAll();

        Assert.All(tracker.Entries, entry => Assert.True(entry.IsCompleted));
    }

    [Fact]
    public void CompleteAll_PreservesStepsReportedAsSkipped()
    {
        var tracker = CreateTracker();
        tracker.Reset(["Prepare", "Apply"]);
        tracker.Apply(CreateProgress(1, DeploymentStepState.Skipped));

        tracker.CompleteAll();

        Assert.Equal(DeploymentStepState.Skipped, tracker.Entries[0].State);
        Assert.Equal(DeploymentStepState.Succeeded, tracker.Entries[1].State);
    }

    [Fact]
    public void Apply_ExposesLocalizedStateTextForAutomation()
    {
        var tracker = CreateTracker();
        tracker.Reset(["Prepare"]);

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
