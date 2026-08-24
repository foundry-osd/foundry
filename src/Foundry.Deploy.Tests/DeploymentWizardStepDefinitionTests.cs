// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Wizard;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentWizardStepDefinitionTests
{
    [Fact]
    public void CreateSequence_ExcludesAutopilotWhenBootMediaDoesNotConfigureIt()
    {
        IReadOnlyList<DeploymentWizardStepDefinition> steps =
            DeploymentWizardStepDefinition.CreateSequence(includeAutopilot: false);

        Assert.Equal(
            [
                DeploymentWizardStepId.TargetDevice,
                DeploymentWizardStepId.OperatingSystem,
                DeploymentWizardStepId.Drivers,
                DeploymentWizardStepId.Summary
            ],
            steps.Select(step => step.Id));
    }

    [Fact]
    public void CreateSequence_IncludesAutopilotImmediatelyBeforeSummary()
    {
        IReadOnlyList<DeploymentWizardStepDefinition> steps =
            DeploymentWizardStepDefinition.CreateSequence(includeAutopilot: true);

        Assert.Equal(DeploymentWizardStepId.Autopilot, steps[^2].Id);
        Assert.Equal(DeploymentWizardStepId.Summary, steps[^1].Id);
    }

    [Fact]
    public void StepViewModel_UsesItsDynamicSequencePosition()
    {
        var definition = new DeploymentWizardStepDefinition(
            DeploymentWizardStepId.OperatingSystem,
            "Wizard.Step.OperatingSystem");

        var step = new DeploymentWizardStepViewModel(
            definition,
            "Operating system",
            displayNumber: 2,
            isLast: true);

        Assert.Equal(2, step.DisplayNumber);
        Assert.True(step.IsLast);
    }
}
