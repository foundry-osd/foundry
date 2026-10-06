// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Updates;

/// <summary>
/// Coordinates application update discovery, preparation, and application handoff.
/// </summary>
public interface IApplicationUpdateService
{
    /// <summary>
    /// Initializes once and recovers prepared updates in the background without blocking application launch.
    /// When enabled, startup work checks and downloads without requesting restart.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels initialization before background work is scheduled.</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the configured update feed for a newer release and publishes the result to update state.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the check.</param>
    /// <returns>The update check result.</returns>
    Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the captured update and publishes its progress and readiness to update state.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the download.</param>
    Task DownloadUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules the captured prepared update for application after process exit without exiting the application.
    /// </summary>
    /// <param name="restart">Whether to show update progress and restart after application, rather than apply silently on close.</param>
    /// <returns>Whether a prepared update was scheduled or had already been scheduled.</returns>
    /// <remarks>
    /// Returns false without a prepared target and propagates handoff failures while retaining readiness.
    /// Also returns false for a silent apply while another Foundry instance is running, leaving the update prepared.
    /// </remarks>
    bool TrySchedulePreparedUpdate(bool restart);

    /// <summary>
    /// Immediately snapshots confirmed readiness, cancels unfinished work, and invalidates queued operation callbacks.
    /// </summary>
    void BeginShutdown();

    /// <summary>
    /// Reopens update requests after a canceled close without reviving invalidated callbacks.
    /// </summary>
    void CancelShutdown();
}
