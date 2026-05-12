namespace Foundry.Services.Updates;

/// <summary>
/// Describes update states emitted by the update service or synthesized by update UI workflows.
/// </summary>
public enum ApplicationUpdateStatus
{
    /// <summary>
    /// The update service is initialized and ready for checks.
    /// </summary>
    Ready,

    /// <summary>
    /// Update checks are intentionally skipped while debugging.
    /// </summary>
    SkippedInDebug,

    /// <summary>
    /// The application is not running from an installed package that supports in-place updates.
    /// </summary>
    NotInstalled,

    /// <summary>
    /// An update check is in progress.
    /// </summary>
    Checking,

    /// <summary>
    /// The current application version is up to date.
    /// </summary>
    NoUpdate,

    /// <summary>
    /// A newer release is available for download.
    /// </summary>
    UpdateAvailable,

    /// <summary>
    /// An available update is being downloaded.
    /// </summary>
    Downloading,

    /// <summary>
    /// An update has been downloaded and can be applied by restarting.
    /// </summary>
    ReadyToRestart,

    /// <summary>
    /// The latest update operation failed.
    /// </summary>
    Failed
}
