// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.ViewModels;

public sealed partial class DeploymentTimelineEntryViewModel : ObservableObject
{
    public DeploymentTimelineEntryViewModel(int stepIndex, string rawName, string displayName, string stateAutomationText)
    {
        StepIndex = stepIndex;
        RawName = rawName;
        RawLabel = rawName;
        this.displayName = displayName;
        this.stateAutomationText = stateAutomationText;
    }

    public int StepIndex { get; internal set; }
    public string RawName { get; private set; }
    public string RawLabel { get; internal set; }
    public string? RawMessage { get; internal set; }

    [ObservableProperty]
    private string detailText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    private DeploymentStepState state = DeploymentStepState.Pending;

    [ObservableProperty]
    private string displayName;

    [ObservableProperty]
    private string stateAutomationText;

    public bool IsActive => State is DeploymentStepState.Running or DeploymentStepState.Failed or DeploymentStepState.Cancelled;

    public string Glyph => State switch
    {
        DeploymentStepState.Succeeded => "\uE930",
        DeploymentStepState.Skipped => "\uE946",
        DeploymentStepState.Running => "\uE915",
        DeploymentStepState.Failed => "\uEA39",
        DeploymentStepState.Cancelled => "\uE711",
        _ => "\uECCA"
    };

    public void Update(string rawName, string displayName, DeploymentStepState newState, string newStateAutomationText)
    {
        RawName = rawName;
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
