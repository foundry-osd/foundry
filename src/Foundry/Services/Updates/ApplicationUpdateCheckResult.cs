namespace Foundry.Services.Updates;

/// <summary>
/// Represents the outcome of an application update check.
/// </summary>
/// <param name="Status">Lifecycle status produced by the check.</param>
/// <param name="Message">User-visible status or failure message.</param>
/// <param name="Version">Available release version, when an update exists.</param>
public sealed record ApplicationUpdateCheckResult(
    ApplicationUpdateStatus Status,
    string Message,
    string? Version = null)
{
    /// <summary>
    /// Gets a value indicating whether the check found a downloadable update.
    /// </summary>
    public bool IsUpdateAvailable => Status == ApplicationUpdateStatus.UpdateAvailable;
}
