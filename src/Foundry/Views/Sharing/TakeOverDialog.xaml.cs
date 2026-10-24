// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Views;

/// <summary>Hosts "Take over"; the view model publishes with the current password, failures stay inline.</summary>
public sealed partial class TakeOverDialog : ContentDialog
{
    public TakeOverDialogViewModel ViewModel { get; }

    public TakeOverDialog(TakeOverDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Keep the dialog open while the take over publishes.
        args.Cancel = true;
        if (await ViewModel.TakeOverAsync()) Hide();
    }

    private void OnCloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args) => args.Cancel = ViewModel.IsBusy;

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (ViewModel.IsBusy) args.Cancel = true;
    }
}
