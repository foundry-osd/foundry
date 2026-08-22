// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Deploy.Services.Wizard;

namespace Foundry.Deploy.ViewModels;

public partial class DeploymentWizardStepViewModel : ObservableObject
{
    public DeploymentWizardStepViewModel(DeploymentWizardStepDefinition definition, string title)
    {
        Definition = definition;
        Title = title;
    }

    public DeploymentWizardStepDefinition Definition { get; }
    public DeploymentWizardStepId Id => Definition.Id;

    [ObservableProperty]
    private string title;

    [ObservableProperty]
    private bool isCompleted;

    [ObservableProperty]
    private bool isCurrent;

    [ObservableProperty]
    private bool isFuture;

    [ObservableProperty]
    private bool isEnabled;

    public string Glyph => IsCompleted ? "\uE930" : IsCurrent ? "\uE915" : "\uECCA";

    partial void OnIsCompletedChanged(bool value)
    {
        OnPropertyChanged(nameof(Glyph));
    }

    partial void OnIsCurrentChanged(bool value)
    {
        OnPropertyChanged(nameof(Glyph));
    }
}
