// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Application;

/// <summary>
/// Abstracts user dialogs from platform-specific UI implementations.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows an informational message dialog.
    /// </summary>
    /// <param name="request">The dialog request.</param>
    Task ShowMessageAsync(DialogRequest request);

    /// <summary>
    /// Shows a confirmation dialog.
    /// </summary>
    /// <param name="request">The confirmation request.</param>
    /// <returns><see langword="true"/> when the primary action was confirmed.</returns>
    Task<bool> ConfirmAsync(ConfirmationDialogRequest request);
}
