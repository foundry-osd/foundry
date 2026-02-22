using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.ViewModels;

public partial class DeploymentStepItemViewModel : ObservableObject
{
    public DeploymentStepItemViewModel(string stepName)
    {
        StepName = stepName;
    }

    public string StepName { get; }

    [ObservableProperty]
    private DeploymentStepState state = DeploymentStepState.Pending;

    [ObservableProperty]
    private string message = string.Empty;
}
