// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

public interface IDeploymentLaunchPreparationService
{
    DeploymentLaunchPreparationResult Prepare(DeploymentLaunchRequest request);
    /// <summary>Consumes domain runtime settings only during pre-confirmation input preparation.</summary>
    DeploymentLaunchPreparationResult Prepare(DeploymentLaunchRequest request,
        Foundry.Core.Models.Configuration.Deploy.DeployDomainJoinSettings? domainJoin);
}
