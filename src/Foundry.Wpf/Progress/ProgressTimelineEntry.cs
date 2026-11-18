// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using CommunityToolkit.Mvvm.ComponentModel;

namespace Foundry.Wpf.Progress;

/// <summary>Progress page timeline step, owned and localized by the host; <see cref="RawName"/> is its stable identity.</summary>
public sealed partial class ProgressTimelineEntry : ObservableObject
{
    public ProgressTimelineEntry(int stepIndex, string rawName, string displayName, string stateAutomationText)
    {
        StepIndex = stepIndex;
        RawName = rawName;
        RawLabel = rawName;
        this.displayName = displayName;
        this.stateAutomationText = stateAutomationText;
    }

    public int StepIndex { get; set; }
    public string RawName { get; }
    public string RawLabel { get; set; }
    public string? RawMessage { get; set; }

    [ObservableProperty]
    private string detailText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    private ProgressStepState state = ProgressStepState.Pending;

    [ObservableProperty]
    private string displayName;

    [ObservableProperty]
    private string stateAutomationText;

    public bool IsActive => State is ProgressStepState.Running or ProgressStepState.Failed or ProgressStepState.Cancelled;

    public string Glyph => State switch
    {
        ProgressStepState.Succeeded => "\uE930",
        ProgressStepState.Skipped => "\uE946",
        ProgressStepState.Running => "\uE915",
        ProgressStepState.Failed => "\uEA39",
        ProgressStepState.Cancelled => "\uE711",
        _ => "\uECCA"
    };

    public void Update(string displayName, ProgressStepState newState, string newStateAutomationText)
    {
        DisplayName = displayName;
        State = newState;
        StateAutomationText = newStateAutomationText;
    }

    public void RefreshLocalization(string localizedDisplayName, string localizedStateAutomationText)
    {
        DisplayName = localizedDisplayName;
        StateAutomationText = localizedStateAutomationText;
    }
}
