// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Updates;

/// <summary>
/// Represents the current discovery, download, or application state of a captured update.
/// </summary>
/// <param name="Status">Lifecycle status produced by the current update operation.</param>
/// <param name="Message">User-visible status or failure message.</param>
/// <param name="Version">Available release version, when an update exists.</param>
/// <param name="DownloadProgress">Actual SDK download percentage, from zero to one hundred.</param>
/// <param name="FailureMessage">Actionable error from the latest failed operation, when present.</param>
public sealed record ApplicationUpdateCheckResult(
    ApplicationUpdateStatus Status,
    string Message,
    string? Version = null,
    int DownloadProgress = 0,
    string? FailureMessage = null)
{
    /// <summary>
    /// Gets a value indicating whether a target is known, including during a refresh or after a failed download.
    /// </summary>
    public bool HasKnownUpdate => !string.IsNullOrWhiteSpace(Version)
        && Status is ApplicationUpdateStatus.UpdateAvailable
            or ApplicationUpdateStatus.Checking
            or ApplicationUpdateStatus.Downloading
            or ApplicationUpdateStatus.ReadyToApply
            or ApplicationUpdateStatus.Failed;

    /// <summary>
    /// Gets a value indicating whether the captured update has completed SDK preparation.
    /// </summary>
    public bool IsReadyToApply => HasKnownUpdate && Status == ApplicationUpdateStatus.ReadyToApply;

    /// <summary>
    /// Gets a value indicating whether a check or download is running.
    /// </summary>
    public bool IsBusy => Status is ApplicationUpdateStatus.Checking or ApplicationUpdateStatus.Downloading;
}
