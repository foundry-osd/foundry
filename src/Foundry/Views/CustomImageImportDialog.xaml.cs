// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;

namespace Foundry.Views;

public sealed partial class CustomImageImportDialog : ContentDialog
{
    private bool closeRequested;
    private bool closed;
    public CustomImageImportViewModel ViewModel { get; }

    public CustomImageImportDialog(CustomImageImportViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Keep the close button usable while the view model disables editing and performs the import.
        args.Cancel = true;
        if (await ViewModel.ImportAsync() && !closed) Hide();
    }

    private void OnCloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!ViewModel.IsBusy) return;
        args.Cancel = true;
        RequestClose();
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (!ViewModel.IsBusy) return;
        args.Cancel = true;
        RequestClose();
    }

    private void RequestClose()
    {
        closeRequested = true;
        ViewModel.Cancel();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ViewModel.IsBusy) && !ViewModel.IsBusy && closeRequested && !closed)
            Hide();
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        closed = true;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }
}
