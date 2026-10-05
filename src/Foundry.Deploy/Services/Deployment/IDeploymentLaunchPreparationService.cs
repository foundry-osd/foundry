// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

public interface IDeploymentLaunchPreparationService
{
    DeploymentLaunchPreparationResult Prepare(DeploymentLaunchRequest request);
    /// <summary>
    /// Consumes domain runtime settings and the technician's wizard input only during pre-confirmation preparation;
    /// the caller keeps ownership of <paramref name="domainJoinSubmission"/>.
    /// </summary>
    DeploymentLaunchPreparationResult Prepare(DeploymentLaunchRequest request,
        Foundry.Core.Models.Configuration.Deploy.DeployDomainJoinSettings? domainJoin,
        Foundry.Deploy.Services.DomainJoin.DomainJoinSubmission? domainJoinSubmission = null);
}
