// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.ViewModels;

public sealed class DeploymentTimelineTracker
{
    private readonly Func<string, string> _localizeStepName;

    public DeploymentTimelineTracker(Func<string, string> localizeStepName)
    {
        _localizeStepName = localizeStepName ?? throw new ArgumentNullException(nameof(localizeStepName));
    }

    public ObservableCollection<DeploymentTimelineEntryViewModel> Entries { get; } = [];

    public void Reset(IReadOnlyList<string> plannedSteps)
    {
        ArgumentNullException.ThrowIfNull(plannedSteps);
        Entries.Clear();
        for (int index = 0; index < plannedSteps.Count; index++)
        {
            string name = plannedSteps[index];
            Entries.Add(new DeploymentTimelineEntryViewModel(index + 1, name, _localizeStepName(name)));
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

        Entries[index].Update(progress.StepName, _localizeStepName(progress.StepName), progress.State);
    }

    public void FailAt(int oneBasedStepIndex)
    {
        int index = oneBasedStepIndex - 1;
        if (index >= 0 && index < Entries.Count)
        {
            Entries[index].State = DeploymentStepState.Failed;
        }
    }

    public void CompleteAll()
    {
        foreach (DeploymentTimelineEntryViewModel entry in Entries)
        {
            entry.State = DeploymentStepState.Succeeded;
        }
    }

    public void RefreshLocalization()
    {
        foreach (DeploymentTimelineEntryViewModel entry in Entries)
        {
            entry.DisplayName = _localizeStepName(entry.RawName);
        }
    }
}
