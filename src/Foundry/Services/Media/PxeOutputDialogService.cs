// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Application;
using Foundry.Core.Services.Media;
using Foundry.Services.Localization;

namespace Foundry.Services.Media;

/// <inheritdoc />
internal sealed class PxeOutputDialogService(IFilePickerService picker, IApplicationLocalizationService localization)
    : IPxeOutputDialogService
{
    /// <inheritdoc />
    public async Task<string?> ShowAsync(string initialFolderPath, PxeMissingContent missingContent)
    {
        var viewModel = new PxeOutputDialogViewModel(picker, localization, initialFolderPath, missingContent);
        var dialog = new PxeOutputDialog(viewModel, App.MainWindow.Content.XamlRoot);
        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary && viewModel.CanCreate ? viewModel.FolderPath.Trim() : null;
    }
}
