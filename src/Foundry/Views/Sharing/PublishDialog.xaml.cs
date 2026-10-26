// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Views;

/// <summary>Hosts "Publish to a folder"; the view model validates and publishes, failures stay inline.</summary>
public sealed partial class PublishDialog : ContentDialog
{
    public PublishDialogViewModel ViewModel { get; }

    public PublishDialog(PublishDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Keep the dialog open while the first publish runs.
        args.Cancel = true;
        if (await ViewModel.PublishAsync()) Hide();
    }

    private void OnCloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args) => args.Cancel = ViewModel.IsBusy;

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (ViewModel.IsBusy) args.Cancel = true;
    }
}
