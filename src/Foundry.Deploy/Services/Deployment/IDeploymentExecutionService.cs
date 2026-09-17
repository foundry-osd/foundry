// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

public interface IDeploymentExecutionService
{
    /// <summary>
    /// Executes an authorized deployment, observing cancellation only after active mutation and cleanup finish safely.
    /// </summary>
    Task<DeploymentExecutionRunResult> ExecuteAsync(DeploymentContext context, CancellationToken cancellationToken = default);
}
