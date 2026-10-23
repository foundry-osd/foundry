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
    /// <summary>Shows the driver set import dialog.</summary>
    /// <returns>
    /// The new set to add, or <see langword="null"/> when nothing was imported; <c>ExistingSetId</c> names the configuration set that already
    /// holds the content of the last attempt, if any.
    /// </returns>
    public async Task<(CustomDriverSetImportResult? Imported, string? ExistingSetId)> ShowAsync()
    {
        using var viewModel = new CustomDriverSetImportViewModel(library, picker, state, localization);
        var dialog = new CustomDriverSetImportDialog(viewModel) { XamlRoot = App.MainWindow.Content.XamlRoot };
        await dialog.ShowAsync();
        return (viewModel.Result, viewModel.ExistingSetId);
    }
}
