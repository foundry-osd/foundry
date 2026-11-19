// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Catalog;

namespace Foundry.Deploy.Services.Startup;

public interface IDeploymentStartupCoordinator
{
    /// <summary>
    /// Prepares startup with best-effort tag discovery; deliberate caller cancellation is not a fallback. May run again after a
    /// network cache startup failure: each run closes the previous network cache session and resolves a new one.
    /// </summary>
    Task<DeploymentStartupSnapshot> InitializeAsync(DeploymentStartupRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads only the operating system and driver pack catalogs again, after <see cref="DeploymentStartupSnapshot.CatalogFailure"/>
    /// reported that they could not be loaded. Throws the load failure again when the catalogs are still unavailable.
    /// </summary>
    Task<DeploymentCatalogSnapshot> ReloadCatalogsAsync(CancellationToken cancellationToken = default);
}
