// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using CommunityToolkit.Mvvm.ComponentModel;

namespace Foundry.Wpf.Progress;

/// <summary>Progress page timeline step, owned and localized by the host; <see cref="RawName"/> is its stable identity.</summary>
public sealed partial class ProgressTimelineEntry : ObservableObject
{
    /// <summary>Initializes a pending timeline entry whose raw label starts as <paramref name="rawName"/>.</summary>
    public ProgressTimelineEntry(int stepIndex, string rawName, string displayName, string stateAutomationText)
    {
        StepIndex = stepIndex;
        RawName = rawName;
        RawLabel = rawName;
        this.displayName = displayName;
        this.stateAutomationText = stateAutomationText;
    }

    /// <summary>Gets or sets the zero-based position of the step in the timeline.</summary>
    public int StepIndex { get; set; }
    /// <summary>Gets the stable step identity; it never changes after construction.</summary>
    public string RawName { get; }
    /// <summary>Gets or sets the host-provided label used as the localization source; it can change.</summary>
    public string RawLabel { get; set; }
    /// <summary>Gets or sets the latest raw message reported by the host for this step.</summary>
    public string? RawMessage { get; set; }

    /// <summary>Gets or sets the step detail shown as the tooltip and automation help text.</summary>
    [ObservableProperty]
    private string detailText = string.Empty;

    /// <summary>Gets or sets the step state; it drives <see cref="IsActive"/> and <see cref="Glyph"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    private ProgressStepState state = ProgressStepState.Pending;

    /// <summary>Gets or sets the localized step name shown in the timeline.</summary>
    [ObservableProperty]
    private string displayName;

    /// <summary>Gets or sets the localized state text exposed to UI automation.</summary>
    [ObservableProperty]
    private string stateAutomationText;

    /// <summary>Gets a value indicating whether the step is running, failed or cancelled and should be emphasized.</summary>
    public bool IsActive => State is ProgressStepState.Running or ProgressStepState.Failed or ProgressStepState.Cancelled;

    /// <summary>Gets the Segoe Fluent Icons glyph matching the current <see cref="State"/>.</summary>
    public string Glyph => State switch
    {
        ProgressStepState.Succeeded => "\uE930",
        ProgressStepState.Skipped => "\uE946",
        ProgressStepState.Running => "\uE915",
        ProgressStepState.Failed => "\uEA39",
        ProgressStepState.Cancelled => "\uE711",
        _ => "\uECCA"
    };

    /// <summary>Applies a new display name, state and automation text in one call.</summary>
    public void Update(string displayName, ProgressStepState newState, string newStateAutomationText)
    {
        DisplayName = displayName;
        State = newState;
        StateAutomationText = newStateAutomationText;
    }

    /// <summary>Replaces the display name and automation text after a culture change, leaving the state untouched.</summary>
    public void RefreshLocalization(string localizedDisplayName, string localizedStateAutomationText)
    {
        DisplayName = localizedDisplayName;
        StateAutomationText = localizedStateAutomationText;
    }
}
