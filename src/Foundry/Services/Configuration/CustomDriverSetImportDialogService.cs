// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Application;
using Foundry.Core.Services.Drivers;
using Foundry.Services.Localization;

namespace Foundry.Services.Configuration;

/// <summary>Hosts the modal custom driver set import while its view model owns the cancellable library operation.</summary>
public sealed class CustomDriverSetImportDialogService(CustomDriverSetLibrary library, IFilePickerService picker,
    IFoundryConfigurationStateService state, IApplicationLocalizationService localization)
{
    /// <summary>Shows the import dialog for the active profile.</summary>
    /// <returns>The new set to add, or <see langword="null"/> when the dialog was canceled or the content is already in the profile.</returns>
    public async Task<CustomDriverSetImportResult?> ShowAsync()
    {
        using var viewModel = new CustomDriverSetImportViewModel(library, picker, state, localization);
        var dialog = new CustomDriverSetImportDialog(viewModel) { XamlRoot = App.MainWindow.Content.XamlRoot };
        await dialog.ShowAsync();
        return viewModel.Result;
    }
}
