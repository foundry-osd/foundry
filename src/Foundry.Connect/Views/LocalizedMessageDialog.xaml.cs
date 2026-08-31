// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;

namespace Foundry.Connect.Views;

public partial class LocalizedMessageDialog : Window
{
    public LocalizedMessageDialog(
        string title,
        string message,
        string primaryButtonText,
        string? cancelButtonText = null)
    {
        DialogTitle = title;
        Message = message;
        PrimaryButtonText = primaryButtonText;
        CancelButtonText = cancelButtonText ?? string.Empty;
        CancelButtonVisibility = cancelButtonText is null ? Visibility.Collapsed : Visibility.Visible;
        InitializeComponent();
        Owner = Application.Current?.MainWindow;
    }

    public string DialogTitle { get; }

    public string Message { get; }

    public string PrimaryButtonText { get; }

    public string CancelButtonText { get; }

    public Visibility CancelButtonVisibility { get; }

    private void PrimaryButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
