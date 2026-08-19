// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;

namespace Foundry.Deploy.Views;

public partial class DeploymentPasswordDialog : Window
{
    private char[] password = [];
    private bool isPasswordRevealed;
    private bool isSynchronizingPasswordEditors;

    public static readonly DependencyProperty HeadingTextProperty = DependencyProperty.Register(
        nameof(HeadingText), typeof(string), typeof(DeploymentPasswordDialog), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DescriptionTextProperty = DependencyProperty.Register(
        nameof(DescriptionText), typeof(string), typeof(DeploymentPasswordDialog), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty PasswordPlaceholderProperty = DependencyProperty.Register(
        nameof(PasswordPlaceholder), typeof(string), typeof(DeploymentPasswordDialog), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ContinueTextProperty = DependencyProperty.Register(
        nameof(ContinueText), typeof(string), typeof(DeploymentPasswordDialog), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty CancelTextProperty = DependencyProperty.Register(
        nameof(CancelText), typeof(string), typeof(DeploymentPasswordDialog), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty TogglePasswordVisibilityTextProperty = DependencyProperty.Register(
        nameof(TogglePasswordVisibilityText), typeof(string), typeof(DeploymentPasswordDialog), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ErrorTextProperty = DependencyProperty.Register(
        nameof(ErrorText), typeof(string), typeof(DeploymentPasswordDialog), new PropertyMetadata(string.Empty));

    public DeploymentPasswordDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordInput.Focus();
        Closed += (_, _) => ClearPasswordEditors();
    }

    public string HeadingText { get => (string)GetValue(HeadingTextProperty); set => SetValue(HeadingTextProperty, value); }

    public string DescriptionText { get => (string)GetValue(DescriptionTextProperty); set => SetValue(DescriptionTextProperty, value); }

    public string PasswordPlaceholder { get => (string)GetValue(PasswordPlaceholderProperty); set => SetValue(PasswordPlaceholderProperty, value); }

    public string ContinueText { get => (string)GetValue(ContinueTextProperty); set => SetValue(ContinueTextProperty, value); }

    public string CancelText { get => (string)GetValue(CancelTextProperty); set => SetValue(CancelTextProperty, value); }

    public string TogglePasswordVisibilityText { get => (string)GetValue(TogglePasswordVisibilityTextProperty); set => SetValue(TogglePasswordVisibilityTextProperty, value); }

    public string ErrorText { get => (string)GetValue(ErrorTextProperty); set => SetValue(ErrorTextProperty, value); }

    public char[] TakePassword()
    {
        char[] value = password;
        password = [];
        return value;
    }

    private void PasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        ClearErrorAfterUserInput();
    }

    private void PasswordRevealInput_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ClearErrorAfterUserInput();
    }

    private void ClearErrorAfterUserInput()
    {
        if (!isSynchronizingPasswordEditors)
        {
            ErrorText = string.Empty;
        }
    }

    private void PasswordRevealButton_OnClick(object sender, RoutedEventArgs e)
    {
        isSynchronizingPasswordEditors = true;
        try
        {
            if (isPasswordRevealed)
            {
                PasswordInput.Password = PasswordRevealInput.Text;
                PasswordRevealInput.Text = string.Empty;
                PasswordRevealInput.Visibility = Visibility.Collapsed;
                PasswordInput.Visibility = Visibility.Visible;
                PasswordRevealButton.ClearValue(StyleProperty);
                PasswordInput.Focus();
            }
            else
            {
                PasswordRevealInput.Text = PasswordInput.Password;
                PasswordInput.Password = string.Empty;
                PasswordInput.Visibility = Visibility.Collapsed;
                PasswordRevealInput.Visibility = Visibility.Visible;
                PasswordRevealButton.SetResourceReference(StyleProperty, "AccentButtonStyle");
                PasswordRevealInput.Focus();
                PasswordRevealInput.CaretIndex = PasswordRevealInput.Text.Length;
            }

            isPasswordRevealed = !isPasswordRevealed;
        }
        finally
        {
            isSynchronizingPasswordEditors = false;
        }
    }

    private void ContinueButton_OnClick(object sender, RoutedEventArgs e)
    {
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
        password = (isPasswordRevealed ? PasswordRevealInput.Text : PasswordInput.Password).ToCharArray();
        ClearPasswordEditors();
        DialogResult = true;
    }

    private void ClearPasswordEditors()
    {
        isSynchronizingPasswordEditors = true;
        try
        {
            PasswordInput.Password = string.Empty;
            PasswordRevealInput.Text = string.Empty;
        }
        finally
        {
            isSynchronizingPasswordEditors = false;
        }
    }
}
