namespace Foundry.Services.Updates;

/// <summary>
/// Coordinates application update discovery, download, and restart handoff.
/// </summary>
public interface IApplicationUpdateService
{
    /// <summary>
    /// Initializes the update subsystem before any checks are requested.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels initialization.</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the configured update feed for a newer release.
    /// </summary>
    /// <param name="isStartupCheck">Whether the check is part of startup and may be skipped by settings.</param>
    /// <param name="cancellationToken">Token that cancels the check.</param>
    /// <returns>The update check result.</returns>
    Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync(bool isStartupCheck = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the update discovered by the last successful check.
    /// </summary>
    /// <param name="progress">Optional progress receiver for download percentage updates.</param>
    /// <param name="cancellationToken">Token that cancels the download.</param>
    /// <returns>The download result.</returns>
    Task<ApplicationUpdateDownloadResult> DownloadUpdateAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the downloaded update and restarts the application.
    /// </summary>
    void ApplyUpdateAndRestart();
}
