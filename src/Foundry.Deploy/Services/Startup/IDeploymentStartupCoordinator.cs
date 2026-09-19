// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Startup;

public interface IDeploymentStartupCoordinator
{
    /// <summary>Prepares startup with best-effort tag discovery; deliberate caller cancellation is not a fallback.</summary>
    Task<DeploymentStartupSnapshot> InitializeAsync(DeploymentStartupRequest request, CancellationToken cancellationToken = default);
}
