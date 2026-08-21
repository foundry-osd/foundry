// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Wizard;

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
}
