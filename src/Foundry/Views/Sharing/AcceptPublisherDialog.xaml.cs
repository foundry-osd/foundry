// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Views;

/// <summary>Hosts "Accept new reference"; the view model pins the new key, failures stay inline.</summary>
public sealed partial class AcceptPublisherDialog : ContentDialog
{
    public AcceptPublisherDialogViewModel ViewModel { get; }

    public AcceptPublisherDialog(AcceptPublisherDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Keep the dialog open while the publication of the new reference is applied.
        args.Cancel = true;
        if (await ViewModel.AcceptAsync()) Hide();
    }

    private void OnCloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args) => args.Cancel = ViewModel.IsBusy;

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (ViewModel.IsBusy) args.Cancel = true;
    }
}
