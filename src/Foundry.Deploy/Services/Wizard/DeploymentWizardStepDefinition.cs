// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Wizard;

public sealed record DeploymentWizardStepDefinition(
    DeploymentWizardStepId Id,
    string ResourceKey)
{
    private static readonly DeploymentWizardStepDefinition TargetDevice =
        new(DeploymentWizardStepId.TargetDevice, "Wizard.Step.TargetDevice");

    private static readonly DeploymentWizardStepDefinition OperatingSystem =
        new(DeploymentWizardStepId.OperatingSystem, "Wizard.Step.OperatingSystem");

    private static readonly DeploymentWizardStepDefinition Drivers =
        new(DeploymentWizardStepId.Drivers, "Wizard.Step.Drivers");

    private static readonly DeploymentWizardStepDefinition Autopilot =
        new(DeploymentWizardStepId.Autopilot, "Wizard.Step.Autopilot");

    private static readonly DeploymentWizardStepDefinition Summary =
        new(DeploymentWizardStepId.Summary, "Wizard.Step.Summary");

    public static IReadOnlyList<DeploymentWizardStepDefinition> CreateSequence(bool includeAutopilot)
    {
        return includeAutopilot
            ? [TargetDevice, OperatingSystem, Drivers, Autopilot, Summary]
            : [TargetDevice, OperatingSystem, Drivers, Summary];
    }
}
