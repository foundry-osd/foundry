// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Services.Localization;
using Foundry.ViewModels;
using Foundry.Views;

namespace Foundry.Services.Configuration;

public sealed class OobeAdditionalAccountDialogService(
    IApplicationLocalizationService localizationService) : IOobeAdditionalAccountDialogService
{
    public async Task<OobeAdditionalAccountDialogResult?> ShowAsync(
        OobeAdditionalAccountSettings? account,
        IReadOnlyList<OobeAdditionalAccountSettings> existingAccounts,
        char[] initialPassword,
        char[] initialConfirmation)
    {
        ArgumentNullException.ThrowIfNull(existingAccounts);
        ArgumentNullException.ThrowIfNull(initialPassword);
        ArgumentNullException.ThrowIfNull(initialConfirmation);

        var viewModel = new OobeAdditionalAccountDialogViewModel(localizationService, account, existingAccounts);
        var dialog = new OobeAdditionalAccountDialog(viewModel, initialPassword, initialConfirmation)
        {
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        try
        {
            ContentDialogResult result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary
                ? dialog.Result
                : null;
        }
        finally
        {
            dialog.Dispose();
            viewModel.Dispose();
        }
    }
}
