namespace Foundry.Services.Updates;

/// <summary>
/// Coordinates application update discovery, download, and restart handoff.
/// </summary>
public interface IApplicationUpdateService
{
    /// <summary>
    /// Logs update settings and starts the optional startup check without blocking application launch.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels initialization.</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the configured update feed for a newer release and publishes the result to update state.
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
    /// Applies the downloaded update and restarts the application when a pending update exists.
    /// </summary>
    /// <remarks>The call logs and returns when no update has been checked and downloaded.</remarks>
    void ApplyUpdateAndRestart();
}
