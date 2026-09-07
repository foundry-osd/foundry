// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Updates;

/// <summary>
/// Represents the outcome of an application update check.
/// </summary>
/// <param name="Status">Lifecycle status produced by the check.</param>
/// <param name="Message">User-visible status or failure message.</param>
/// <param name="Version">Available release version, when an update exists.</param>
/// <param name="SettingsSaveFailed">Whether persisting the check timestamp failed without invalidating the check result.</param>
public sealed record ApplicationUpdateCheckResult(
    ApplicationUpdateStatus Status,
    string Message,
    string? Version = null,
    bool SettingsSaveFailed = false)
{
    /// <summary>
    /// Gets a value indicating whether the check found a downloadable update.
    /// </summary>
    public bool IsUpdateAvailable => Status == ApplicationUpdateStatus.UpdateAvailable;
}
