// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.ViewModels;

public sealed class DeploymentTimelineTracker
{
    private readonly Func<string, string> _localizeStepName;
    private readonly Func<DeploymentStepState, string> _localizeState;

    public DeploymentTimelineTracker(
        Func<string, string> localizeStepName,
        Func<DeploymentStepState, string> localizeState)
    {
        _localizeStepName = localizeStepName ?? throw new ArgumentNullException(nameof(localizeStepName));
        _localizeState = localizeState ?? throw new ArgumentNullException(nameof(localizeState));
    }

    public ObservableCollection<DeploymentTimelineEntryViewModel> Entries { get; } = [];

    public void Reset(IReadOnlyList<string> plannedSteps)
    {
        ArgumentNullException.ThrowIfNull(plannedSteps);
        Entries.Clear();
        for (int index = 0; index < plannedSteps.Count; index++)
        {
            string name = plannedSteps[index];
            Entries.Add(new DeploymentTimelineEntryViewModel(
                index + 1,
                name,
                _localizeStepName(name),
                _localizeState(DeploymentStepState.Pending)));
        }
    }

    public void Apply(DeploymentStepProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        int index = progress.StepIndex - 1;
        if (index < 0 || index >= Entries.Count)
        {
            return;
        }

        Entries[index].Update(
            progress.StepName,
            _localizeStepName(progress.StepName),
            progress.State,
            _localizeState(progress.State));
    }

    public void FailAt(int oneBasedStepIndex)
    {
        SetState(oneBasedStepIndex, DeploymentStepState.Failed);
    }

    public void SetState(int oneBasedStepIndex, DeploymentStepState state)
    {
        int index = oneBasedStepIndex - 1;
        if (index >= 0 && index < Entries.Count)
        {
            Entries[index].Update(
                Entries[index].RawName,
                Entries[index].DisplayName,
                state,
                _localizeState(state));
        }
    }

    public void CompleteAll()
    {
        foreach (DeploymentTimelineEntryViewModel entry in Entries)
        {
            if (entry.State != DeploymentStepState.Skipped)
            {
                entry.Update(
                    entry.RawName,
                    entry.DisplayName,
                    DeploymentStepState.Succeeded,
                    _localizeState(DeploymentStepState.Succeeded));
            }
        }
    }

    public void RefreshLocalization()
    {
        foreach (DeploymentTimelineEntryViewModel entry in Entries)
        {
            entry.RefreshLocalization(_localizeStepName(entry.RawName), _localizeState(entry.State));
        }
    }
}
