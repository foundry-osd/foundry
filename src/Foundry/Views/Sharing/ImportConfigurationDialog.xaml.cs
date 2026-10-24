// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Views;

/// <summary>Hosts the import dialog; the view model opens the configuration, shows the preview and imports it.</summary>
public sealed partial class ImportConfigurationDialog : ContentDialog
{
    public ImportConfigurationDialogViewModel ViewModel { get; }

    public ImportConfigurationDialog(ImportConfigurationDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Keep the dialog open while the configuration is replaced; failures stay inline.
        args.Cancel = true;
        if (await ViewModel.ImportAsync()) Hide();
    }

    private void OnCloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args) => args.Cancel = ViewModel.IsBusy;

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (ViewModel.IsBusy) args.Cancel = true;
    }
}
