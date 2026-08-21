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
        this.displayName = displayName;
        this.stateAutomationText = stateAutomationText;
    }

    public int StepIndex { get; }
    public string RawName { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    [NotifyPropertyChangedFor(nameof(BrushResourceKey))]
    private DeploymentStepState state = DeploymentStepState.Pending;

    [ObservableProperty]
    private string displayName;

    [ObservableProperty]
    private string stateAutomationText;

    public bool IsCompleted => State is DeploymentStepState.Succeeded or DeploymentStepState.Skipped;
    public bool IsActive => State is DeploymentStepState.Running or DeploymentStepState.Failed;

    public string Glyph => State switch
    {
        DeploymentStepState.Succeeded or DeploymentStepState.Skipped => "\uE930",
        DeploymentStepState.Running => "\uE915",
        DeploymentStepState.Failed => "\uEA39",
        _ => "\uECCA"
    };

    public string BrushResourceKey => State switch
    {
        DeploymentStepState.Succeeded or DeploymentStepState.Skipped => "SystemFillColorSuccessBrush",
        DeploymentStepState.Running => "AccentFillColorDefaultBrush",
        DeploymentStepState.Failed => "SystemFillColorCriticalBrush",
        _ => "TextFillColorSecondaryBrush"
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
