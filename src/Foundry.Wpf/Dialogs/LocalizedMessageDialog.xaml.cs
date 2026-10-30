// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;

namespace Foundry.Wpf.Dialogs;

/// <summary>
/// Modal message window with caller-localized texts, used instead of <see cref="MessageBox"/> to keep the
/// application styling. The primary button sets <c>DialogResult</c> to <see langword="true"/>; the cancel
/// button is only shown when cancel text is supplied. The window is owned by the main window when one exists.
/// </summary>
public partial class LocalizedMessageDialog : Window
{
    /// <param name="title">Window title.</param>
    /// <param name="message">Message body.</param>
    /// <param name="primaryButtonText">Caption of the confirming button.</param>
    /// <param name="cancelButtonText">Caption of the cancel button; <see langword="null"/> hides the button.</param>
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
