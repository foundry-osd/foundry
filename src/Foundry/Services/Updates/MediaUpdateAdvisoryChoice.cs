// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Updates;

/// <summary>
/// Describes the explicit decision made before a media authoring request proceeds.
/// </summary>
public enum MediaUpdateAdvisoryChoice
{
    /// <summary>
    /// Ends the media request without further work.
    /// </summary>
    Cancel,

    /// <summary>
    /// Ends the media request and opens update settings.
    /// </summary>
    ViewUpdate,

    /// <summary>
    /// Ends the media request and requests the protected update and restart flow.
    /// </summary>
    ApplyUpdate,

    /// <summary>
    /// Continues media authoring after revalidating the captured target.
    /// </summary>
    CreateAnyway
}
