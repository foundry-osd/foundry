using Foundry.Deploy.Services.Wizard;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentWizardStateServiceTests
{
    [Fact]
    public void CanGoNext_WhenFirstStepIsStillLoadingCatalog_ReturnsFalse()
    {
        var service = new DeploymentWizardStateService();

        bool canGoNext = service.CanGoNext(CreateSnapshot(wizardStepIndex: 0, isCatalogLoading: true));

        Assert.False(canGoNext);
    }

    [Fact]
    public void CanStartDeployment_WhenDebugSafeModeHasNoDiskSelection_ReturnsTrue()
    {
        var service = new DeploymentWizardStateService();

        bool canStart = service.CanStartDeployment(
            CreateSnapshot(
                wizardStepIndex: 3,
                isDebugSafeMode: true,
                hasSelectedOperatingSystem: true,
                hasTargetDiskSelection: false,
                isTargetComputerNameValid: true,
                hasValidDriverPackSelection: true,
                hasValidAutopilotSelection: true));

        Assert.True(canStart);
    }

    [Fact]
    public void CanStartDeployment_WhenSelectedDiskIsBlockedOutsideDebugMode_ReturnsFalse()
    {
        var service = new DeploymentWizardStateService();

        bool canStart = service.CanStartDeployment(
            CreateSnapshot(
                wizardStepIndex: 3,
                hasSelectedOperatingSystem: true,
                hasTargetDiskSelection: true,
                isSelectedTargetDiskSelectable: false,
                isTargetComputerNameValid: true,
                hasValidDriverPackSelection: true,
                hasValidAutopilotSelection: true));

        Assert.False(canStart);
    }

    private static DeploymentWizardStateSnapshot CreateSnapshot(
        int wizardStepIndex,
        bool isDeploymentRunning = false,
        bool isCatalogLoading = false,
        bool isTargetDiskLoading = false,
        bool isDebugSafeMode = false,
        bool isTargetComputerNameValid = false,
        bool hasSelectedOperatingSystem = false,
        bool hasTargetDiskSelection = false,
        bool isSelectedTargetDiskSelectable = true,
        bool hasValidDriverPackSelection = false,
        bool hasValidAutopilotSelection = false,
        bool isOperatingSystemCatalogReadyForNavigation = true)
    {
        return new DeploymentWizardStateSnapshot
        {
            WizardStepIndex = wizardStepIndex,
            IsDeploymentRunning = isDeploymentRunning,
            IsCatalogLoading = isCatalogLoading,
            IsTargetDiskLoading = isTargetDiskLoading,
            IsDebugSafeMode = isDebugSafeMode,
            IsTargetComputerNameValid = isTargetComputerNameValid,
            HasSelectedOperatingSystem = hasSelectedOperatingSystem,
            HasTargetDiskSelection = hasTargetDiskSelection,
            IsSelectedTargetDiskSelectable = isSelectedTargetDiskSelectable,
            HasValidDriverPackSelection = hasValidDriverPackSelection,
            HasValidAutopilotSelection = hasValidAutopilotSelection,
            IsOperatingSystemCatalogReadyForNavigation = isOperatingSystemCatalogReadyForNavigation
        };
    }
}
