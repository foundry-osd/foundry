// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Wizard;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentWizardNavigationStateTests
{
    [Fact]
    public void TryNavigateTo_AllowsCompletedStepButRejectsFutureStep()
    {
        var state = new DeploymentWizardNavigationState(
            DeploymentWizardStepDefinition.CreateSequence(includeAutopilot: false));
        state.Advance();

        Assert.True(state.TryNavigateTo(DeploymentWizardStepId.TargetDevice));
        Assert.False(state.TryNavigateTo(DeploymentWizardStepId.Drivers));
    }

    [Fact]
    public void BeginSummaryEdit_ReturnsDirectlyToSummary()
    {
        var state = new DeploymentWizardNavigationState(
            DeploymentWizardStepDefinition.CreateSequence(includeAutopilot: false));
        state.Advance();
        state.Advance();
        state.Advance();

        Assert.True(state.BeginSummaryEdit(DeploymentWizardStepId.OperatingSystem));
        Assert.Equal(DeploymentWizardStepId.OperatingSystem, state.CurrentStepId);
        Assert.True(state.IsReturningToSummary);

        state.Advance();

        Assert.Equal(DeploymentWizardStepId.Summary, state.CurrentStepId);
        Assert.False(state.IsReturningToSummary);
    }

    [Fact]
    public void ReplaceSteps_PreservesCurrentStepWhenAutopilotIsInserted()
    {
        var state = new DeploymentWizardNavigationState(
            DeploymentWizardStepDefinition.CreateSequence(includeAutopilot: false));
        state.Advance();

        state.ReplaceSteps(DeploymentWizardStepDefinition.CreateSequence(includeAutopilot: true));

        Assert.Equal(DeploymentWizardStepId.OperatingSystem, state.CurrentStepId);
    }

    [Fact]
    public void HasAdvancedPast_RemainsTrueAfterNavigatingBackToCompletedStep()
    {
        var state = new DeploymentWizardNavigationState(
            DeploymentWizardStepDefinition.CreateSequence(includeAutopilot: false));
        state.Advance();
        state.Advance();
        state.Advance();

        Assert.True(state.TryNavigateTo(DeploymentWizardStepId.OperatingSystem));

        Assert.True(state.HasAdvancedPast(DeploymentWizardStepId.OperatingSystem));
    }
}
