// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;

namespace Foundry.Views;

public sealed partial class CustomImageImportDialog : ContentDialog
{
    private const double DialogChromeWidth = 96;
    private const double PreferredContentWidth = 720;
    private const double FallbackMinimumContentWidth = 560;
    private bool closeRequested;
    private bool closed;
    public CustomImageImportViewModel ViewModel { get; }

    public CustomImageImportDialog(CustomImageImportViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ApplyContentWidth();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// Widens the dialog beyond the default ContentDialog maximum so the index preview table has room for its columns,
    /// while keeping it within the main window.
    /// </summary>
    private void ApplyContentWidth()
    {
        double minimumWidth = App.Current.Resources.TryGetValue("FoundryDialogMinWidth", out object value) && value is double width
            ? width
            : FallbackMinimumContentWidth;
        double availableWidth = Math.Max(minimumWidth, App.MainWindow.AppWindow.Size.Width * 0.72);
        double contentWidth = Math.Clamp(PreferredContentWidth, minimumWidth, availableWidth);

        Resources["ContentDialogMaxWidth"] = contentWidth + DialogChromeWidth;
        DialogContentRoot.Width = contentWidth;
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
