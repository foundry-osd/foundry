// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Updates;

/// <summary>
/// Describes update states owned and published by the update service.
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
    /// An update has completed SDK preparation and can be applied after application exit.
    /// </summary>
    ReadyToApply,

    /// <summary>
    /// The latest update operation failed.
    /// </summary>
    Failed
}
