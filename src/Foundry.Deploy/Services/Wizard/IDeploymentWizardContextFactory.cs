namespace Foundry.Deploy.Services.Wizard;

public interface IDeploymentWizardContextFactory
{
    DeploymentWizardContext Create(bool isDebugSafeMode);
}
