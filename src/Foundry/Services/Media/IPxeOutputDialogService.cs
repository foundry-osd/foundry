// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Media;

namespace Foundry.Services.Media;

/// <summary>
/// Asks for the folder that receives PXE boot files.
/// </summary>
public interface IPxeOutputDialogService
{
    /// <summary>
    /// Shows the PXE output dialog.
    /// </summary>
    /// <param name="initialFolderPath">Folder shown when the dialog opens, usually the last one used.</param>
    /// <param name="missingContent">Imported content that a PXE start does not receive, listed as a warning.</param>
    /// <returns>The trimmed folder to write to, or <see langword="null"/> when the user cancels.</returns>
    Task<string?> ShowAsync(string initialFolderPath, PxeMissingContent missingContent);
}
