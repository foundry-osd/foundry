namespace Foundry.Deploy.Services.Wizard;

public interface IDeploymentWizardStateService
{
    bool CanGoPrevious(DeploymentWizardStateSnapshot snapshot);
    bool CanGoNext(DeploymentWizardStateSnapshot snapshot);
    bool CanStartDeployment(DeploymentWizardStateSnapshot snapshot);
}
