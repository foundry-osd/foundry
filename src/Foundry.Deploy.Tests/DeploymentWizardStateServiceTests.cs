// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Wizard;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentWizardStateServiceTests
{
    [Fact]
    public void CanGoNext_WhenFirstStepIsStillLoadingCatalog_ReturnsFalse()
    {
        var service = new DeploymentWizardStateService();

        bool canGoNext = service.CanGoNext(CreateSnapshot(
            currentStepId: DeploymentWizardStepId.TargetDevice,
            isCatalogLoading: true));

        Assert.False(canGoNext);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DomainJoinStep_BlocksNextAndStartUntilItsInputIsValid(bool hasValidDomainJoinInput)
    {
        var service = new DeploymentWizardStateService();
        DeploymentWizardStateSnapshot onStep = CreateSnapshot(currentStepId: DeploymentWizardStepId.DomainJoin) with
        {
            AvailableSteps = DeploymentWizardStepDefinition.CreateSequence(includeAutopilot: false, includeDomainJoin: true),
            HasValidDomainJoinInput = hasValidDomainJoinInput
        };
        DeploymentWizardStateSnapshot onSummary = CreateSnapshot(
            currentStepId: DeploymentWizardStepId.Summary,
            hasSelectedOperatingSystem: true,
            hasTargetDiskSelection: true,
            isTargetComputerNameValid: true,
            hasValidDriverPackSelection: true,
            hasValidAutopilotSelection: true) with
        { HasValidDomainJoinInput = hasValidDomainJoinInput };

        Assert.Equal(hasValidDomainJoinInput, service.CanGoNext(onStep));
        Assert.Equal(hasValidDomainJoinInput, service.CanStartDeployment(onSummary));
    }

    [Fact]
    public void CanStartDeployment_WhenDebugSafeModeHasNoDiskSelection_ReturnsTrue()
    {
        var service = new DeploymentWizardStateService();

        bool canStart = service.CanStartDeployment(
            CreateSnapshot(
                currentStepId: DeploymentWizardStepId.Summary,
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
                currentStepId: DeploymentWizardStepId.Summary,
                hasSelectedOperatingSystem: true,
                hasTargetDiskSelection: true,
                isSelectedTargetDiskSelectable: false,
                isTargetComputerNameValid: true,
                hasValidDriverPackSelection: true,
                hasValidAutopilotSelection: true));

        Assert.False(canStart);
    }

    [Theory]
    [InlineData(DeploymentWizardStepId.TargetDevice, false, true)]
    [InlineData(DeploymentWizardStepId.OperatingSystem, false, false)]
    [InlineData(DeploymentWizardStepId.OperatingSystem, true, true)]
    public void CustomMode_AllowsReachingImagePickerButRequiresSelectionToContinue(DeploymentWizardStepId step, bool selected, bool expected)
    {
        var snapshot = CreateSnapshot(step, hasSelectedOperatingSystem: selected) with { IsCustomImageMode = true };
        Assert.Equal(expected, new DeploymentWizardStateService().CanGoNext(snapshot));
    }

    private static DeploymentWizardStateSnapshot CreateSnapshot(
        DeploymentWizardStepId currentStepId,
        bool includeAutopilot = false,
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
            CurrentStepId = currentStepId,
            AvailableSteps = DeploymentWizardStepDefinition.CreateSequence(includeAutopilot),
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
