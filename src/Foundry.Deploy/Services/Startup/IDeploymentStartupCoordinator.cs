// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Cache;
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

    /// <summary>
    /// Runs the Autopilot group tag discovery again on the configuration the last <see cref="InitializeAsync"/> loaded, after
    /// <see cref="DeploymentStartupSnapshot.AutopilotGroupTagsFailed"/> reported that it failed. A failure is logged and reported
    /// in the result, never thrown; the caller's cancellation is thrown.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="InitializeAsync"/> has not loaded a configuration.</exception>
    Task<AutopilotGroupTagRefresh> RefreshAutopilotGroupTagsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether the network cache should be resolved again because the network may have changed: the current session has no
    /// usable share (it failed, including when the operator continued without it) or was resolved for other local addresses.
    /// <see langword="false"/> when no session was resolved (debug-safe mode, no resolver) or the cache is disabled.
    /// </summary>
    bool ShouldResolveNetworkCacheAgain();

    /// <summary>
    /// Closes the current network cache session and resolves a new one, with the content check, for the request and the
    /// configuration of the last <see cref="InitializeAsync"/>. Returns <see langword="null"/> when no session is resolved or no
    /// configuration is loaded, as at startup.
    /// </summary>
    Task<NetworkCacheStartupResult?> ResolveNetworkCacheAsync(CancellationToken cancellationToken = default);
}
