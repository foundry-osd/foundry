// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Images;
using Foundry.Services.Localization;

namespace Foundry.Services.Configuration;

/// <summary>Hosts the modal import flow while its view model owns the cancellable library operation.</summary>
public sealed class CustomImageImportDialogService(CustomImageLibraryService library, IFilePickerService picker,
    IApplicationLocalizationService localization)
{
    public async Task<CustomImageReference?> ShowAsync(IReadOnlyList<string> existingNames)
    {
        using var viewModel = new CustomImageImportViewModel(library, picker, localization, existingNames);
        var dialog = new CustomImageImportDialog(viewModel) { XamlRoot = App.MainWindow.Content.XamlRoot };
        await dialog.ShowAsync();
        return viewModel.Result;
    }
}
