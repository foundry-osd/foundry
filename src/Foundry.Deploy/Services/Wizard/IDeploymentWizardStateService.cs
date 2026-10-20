// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Wizard;

public interface IDeploymentWizardStateService
{
    bool CanGoPrevious(DeploymentWizardStateSnapshot snapshot);
    /// <summary>Gets whether the step list and the summary edit links may move the wizard to another step.</summary>
    bool CanNavigate(DeploymentWizardStateSnapshot snapshot);
    bool CanGoNext(DeploymentWizardStateSnapshot snapshot);
    bool CanStartDeployment(DeploymentWizardStateSnapshot snapshot);
}
