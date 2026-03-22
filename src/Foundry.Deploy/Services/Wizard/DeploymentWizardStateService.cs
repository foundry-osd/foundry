namespace Foundry.Deploy.Services.Wizard;

public sealed class DeploymentWizardStateService : IDeploymentWizardStateService
{
    public bool CanGoPrevious(DeploymentWizardStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return !snapshot.IsDeploymentRunning && snapshot.WizardStepIndex > 0;
    }

    public bool CanGoNext(DeploymentWizardStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.IsDeploymentRunning || snapshot.WizardStepIndex >= 3)
        {
            return false;
        }

        if (snapshot.WizardStepIndex == 0)
        {
            return !snapshot.IsCatalogLoading &&
                   snapshot.IsOperatingSystemCatalogReadyForNavigation;
        }

        return true;
    }

    public bool CanStartDeployment(DeploymentWizardStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        bool hasTargetDisk = snapshot.HasTargetDiskSelection &&
                             (snapshot.IsDebugSafeMode || snapshot.IsSelectedTargetDiskSelectable);

        if (snapshot.IsDebugSafeMode && !snapshot.HasTargetDiskSelection)
        {
            hasTargetDisk = true;
        }

        return !snapshot.IsDeploymentRunning &&
               !snapshot.IsCatalogLoading &&
               !snapshot.IsTargetDiskLoading &&
               snapshot.WizardStepIndex == 3 &&
               snapshot.IsTargetComputerNameValid &&
               snapshot.HasSelectedOperatingSystem &&
               hasTargetDisk &&
               snapshot.HasValidDriverPackSelection &&
               snapshot.HasValidAutopilotSelection;
    }
}
