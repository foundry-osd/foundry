// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Deploy.Services.Wizard;

namespace Foundry.Deploy.ViewModels;

public partial class DeploymentWizardStepViewModel : ObservableObject
{
    public DeploymentWizardStepViewModel(
        DeploymentWizardStepDefinition definition,
        string title,
        int displayNumber,
        bool isFirst,
        bool isLast)
    {
        Definition = definition;
        Title = title;
        DisplayNumber = displayNumber;
        IsFirst = isFirst;
        IsLast = isLast;
    }

    public DeploymentWizardStepDefinition Definition { get; }
    public DeploymentWizardStepId Id => Definition.Id;
    public int DisplayNumber { get; }
    public bool IsFirst { get; }
    public bool IsLast { get; }

    [ObservableProperty]
    private string title;

    [ObservableProperty]
    private bool isCompleted;

    [ObservableProperty]
    private bool isCurrent;

    [ObservableProperty]
    private bool isEnabled;

    [ObservableProperty]
    private bool isPreviousCompleted;
}
