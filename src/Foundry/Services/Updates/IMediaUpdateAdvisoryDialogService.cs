// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Updates;

/// <summary>
/// Offers an explicit update decision before media authoring begins.
/// </summary>
public interface IMediaUpdateAdvisoryDialogService
{
    /// <summary>
    /// Shows the captured versions with a primary action fixed at dialog opening.
    /// </summary>
    /// <param name="currentVersion">Version of the running authoring application.</param>
    /// <param name="availableVersion">Known newer application version.</param>
    /// <param name="canApplyUpdate">Whether the primary action applies the update instead of opening settings.</param>
    Task<MediaUpdateAdvisoryChoice> ShowAsync(string currentVersion, string availableVersion, bool canApplyUpdate);
}
