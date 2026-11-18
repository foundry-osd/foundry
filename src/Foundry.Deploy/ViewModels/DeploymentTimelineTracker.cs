// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using Foundry.Deploy.Services.Deployment;
using Foundry.Wpf.Progress;

namespace Foundry.Deploy.ViewModels;

/// <summary>
/// Keeps the progress page timeline in step with the deployment plan and its step reports, showing each
/// <see cref="DeploymentStepState"/> as the <see cref="ProgressStepState"/> of the same name.
/// </summary>
public sealed class DeploymentTimelineTracker
{
    private readonly Func<string, string> _localizeStepName;
    private readonly Func<ProgressStepState, string> _localizeState;
    private readonly Func<string, string> _localizeMessage;

    public DeploymentTimelineTracker(
        Func<string, string> localizeStepName,
        Func<ProgressStepState, string> localizeState,
        Func<string, string>? localizeMessage = null)
    {
        _localizeStepName = localizeStepName ?? throw new ArgumentNullException(nameof(localizeStepName));
        _localizeState = localizeState ?? throw new ArgumentNullException(nameof(localizeState));
        _localizeMessage = localizeMessage ?? (value => value);
    }

    public ObservableCollection<ProgressTimelineEntry> Entries { get; } = [];

    public void Reset(IReadOnlyList<DeploymentPlanEntry> plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Entries.Clear();
        Reconcile(plan);
    }

    public void Apply(DeploymentStepProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (progress.Plan is not null) Reconcile(progress.Plan);
        ProgressTimelineEntry? entry = Entries.FirstOrDefault(item => item.RawName == progress.StepName);
        if (entry is null)
        {
            return;
        }

        entry.RawLabel = progress.StepLabel ?? entry.RawLabel;
        entry.RawMessage = progress.Message;
        entry.DetailText = _localizeMessage(progress.Message ?? string.Empty);
        ProgressStepState state = ToProgressStepState(progress.State);
        entry.Update(
            _localizeStepName(entry.RawLabel),
            state,
            _localizeState(state));
    }

    /// <summary>Reconciles future work by stable identity while retaining observed outcomes and explanations.</summary>
    public void Reconcile(IReadOnlyList<DeploymentPlanEntry> plan)
    {
        HashSet<string> names = plan.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        foreach (ProgressTimelineEntry entry in Entries.Where(entry => !names.Contains(entry.RawName)).ToArray())
            Entries.Remove(entry);
        for (int index = 0; index < plan.Count; index++)
        {
            DeploymentPlanEntry planned = plan[index];
            ProgressTimelineEntry? entry = Entries.FirstOrDefault(item => item.RawName == planned.Name);
            if (entry is null)
            {
                entry = new ProgressTimelineEntry(index + 1, planned.Name,
                    _localizeStepName(planned.Label), _localizeState(ProgressStepState.Pending));
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
            Entries[index].State is not (ProgressStepState.Pending or ProgressStepState.Running))
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
            ProgressStepState progressState = ToProgressStepState(state);
            Entries[index].Update(
                Entries[index].DisplayName,
                progressState,
                _localizeState(progressState));
        }
    }

    public void RefreshLocalization()
    {
        foreach (ProgressTimelineEntry entry in Entries)
        {
            entry.RefreshLocalization(_localizeStepName(entry.RawLabel), _localizeState(entry.State));
            entry.DetailText = _localizeMessage(entry.RawMessage ?? string.Empty);
        }
    }

    private static ProgressStepState ToProgressStepState(DeploymentStepState state) => state switch
    {
        DeploymentStepState.Pending => ProgressStepState.Pending,
        DeploymentStepState.Running => ProgressStepState.Running,
        DeploymentStepState.Succeeded => ProgressStepState.Succeeded,
        DeploymentStepState.Failed => ProgressStepState.Failed,
        DeploymentStepState.Skipped => ProgressStepState.Skipped,
        DeploymentStepState.Cancelled => ProgressStepState.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unsupported deployment step state.")
    };
}
