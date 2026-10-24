// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Windows.ApplicationModel.DataTransfer;

namespace Foundry.Views;

/// <summary>Hosts the export dialog; the view model owns validation and the export, the view owns the clipboard.</summary>
public sealed partial class ExportConfigurationDialog : ContentDialog
{
    public ExportConfigurationDialogViewModel ViewModel { get; }

    public ExportConfigurationDialog(ExportConfigurationDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private PasswordRevealMode RevealMode(bool isRevealed) => isRevealed ? PasswordRevealMode.Visible : PasswordRevealMode.Peek;

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Keep the dialog open while the destination is chosen and the file written; failures stay inline.
        args.Cancel = true;
        if (await ViewModel.ExportAsync()) Hide();
    }

    private void OnCloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args) => args.Cancel = ViewModel.IsBusy;

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (ViewModel.IsBusy) args.Cancel = true;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(ViewModel.Passphrase);
        Clipboard.SetContent(package);
        ViewModel.MarkCopied();
    }
}
