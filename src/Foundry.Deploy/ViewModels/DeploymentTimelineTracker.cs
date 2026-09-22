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
    private readonly Func<string, string> _localizeMessage;

    public DeploymentTimelineTracker(
        Func<string, string> localizeStepName,
        Func<DeploymentStepState, string> localizeState,
        Func<string, string>? localizeMessage = null)
    {
        _localizeStepName = localizeStepName ?? throw new ArgumentNullException(nameof(localizeStepName));
        _localizeState = localizeState ?? throw new ArgumentNullException(nameof(localizeState));
        _localizeMessage = localizeMessage ?? (value => value);
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
        if (progress.Plan is not null) Reconcile(progress.Plan);
        DeploymentTimelineEntryViewModel? entry = Entries.FirstOrDefault(item => item.RawName == progress.StepName);
        if (entry is null)
        {
            return;
        }

        entry.RawLabel = progress.StepLabel ?? entry.RawLabel;
        entry.RawMessage = progress.Message;
        entry.DetailText = _localizeMessage(progress.Message ?? string.Empty);
        entry.Update(
            progress.StepName,
            _localizeStepName(entry.RawLabel),
            progress.State,
            _localizeState(progress.State));
    }

    /// <summary>Reconciles future work by stable identity while retaining observed outcomes and explanations.</summary>
    public void Reconcile(IReadOnlyList<DeploymentPlanEntry> plan)
    {
        HashSet<string> names = plan.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        foreach (DeploymentTimelineEntryViewModel entry in Entries.Where(entry => !names.Contains(entry.RawName)).ToArray())
            Entries.Remove(entry);
        for (int index = 0; index < plan.Count; index++)
        {
            DeploymentPlanEntry planned = plan[index];
            DeploymentTimelineEntryViewModel? entry = Entries.FirstOrDefault(item => item.RawName == planned.Name);
            if (entry is null)
            {
                entry = new DeploymentTimelineEntryViewModel(index + 1, planned.Name,
                    _localizeStepName(planned.Label), _localizeState(DeploymentStepState.Pending));
                Entries.Insert(index, entry);
            }
            else if (Entries.IndexOf(entry) != index)
            {
                Entries.Move(Entries.IndexOf(entry), index);
            }
            entry.StepIndex = index + 1;
            entry.RawLabel = planned.Label;
            entry.RefreshLocalization(_localizeStepName(planned.Label), _localizeState(entry.State));
        }
    }

    public void FailAt(int oneBasedStepIndex)
    {
        int index = oneBasedStepIndex - 1;
        if (index < 0 || index >= Entries.Count ||
            Entries[index].State is not (DeploymentStepState.Pending or DeploymentStepState.Running))
        {
            return;
        }
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

    public void RefreshLocalization()
    {
        foreach (DeploymentTimelineEntryViewModel entry in Entries)
        {
            entry.RefreshLocalization(_localizeStepName(entry.RawLabel), _localizeState(entry.State));
            entry.DetailText = _localizeMessage(entry.RawMessage ?? string.Empty);
        }
    }
}
