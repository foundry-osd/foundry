// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Views;

/// <summary>Hosts the follower's "Enter password"; the view model checks it against the publication, failures stay inline.</summary>
public sealed partial class EnterPublicationPasswordDialog : ContentDialog
{
    public EnterPublicationPasswordDialogViewModel ViewModel { get; }

    public EnterPublicationPasswordDialog(EnterPublicationPasswordDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Keep the dialog open while the publication is opened with the password.
        args.Cancel = true;
        if (await ViewModel.SubmitAsync()) Hide();
    }

    private void OnCloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args) => args.Cancel = ViewModel.IsBusy;

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (ViewModel.IsBusy) args.Cancel = true;
    }
}
