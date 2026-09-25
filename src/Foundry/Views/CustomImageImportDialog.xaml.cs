// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Views;

public sealed partial class CustomImageImportDialog : ContentDialog
{
    public CustomImageImportViewModel ViewModel { get; }

    public CustomImageImportDialog(CustomImageImportViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();
        try { args.Cancel = !await ViewModel.ImportAsync(); }
        finally { deferral.Complete(); }
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (!ViewModel.IsBusy) return;
        args.Cancel = true;
        ViewModel.Cancel();
    }
}
