// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Services.Localization;

namespace Foundry.Services.DomainJoin;

public sealed class DomainJoinDialogService(IApplicationLocalizationService localization) : IDomainJoinDialogService
{
    public async Task ShowDomainAsync(DomainJoinDomainDialogRequest request, Func<DomainJoinDomainDialogInput, string?> trySave)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(trySave);

        var dialog = new DomainJoinDomainDialog(localization, request, trySave)
        {
            XamlRoot = App.MainWindow.Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    public async Task ShowAddAsync(Func<string, string, string?> tryAdd)
    {
        ArgumentNullException.ThrowIfNull(tryAdd);

        var dialog = new DomainJoinOuAddDialog(localization, tryAdd)
        {
            XamlRoot = App.MainWindow.Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    public async Task<IReadOnlyList<DomainJoinOrganizationalUnitSettings>?> PickAsync(
        IReadOnlyList<DomainJoinOrganizationalUnitSettings> candidates, bool isIncomplete)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var viewModel = new DomainJoinOuSelectionDialogViewModel(localization, candidates, isIncomplete);
        var dialog = new DomainJoinOuSelectionDialog(viewModel)
        {
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? viewModel.GetSelected() : null;
    }
}