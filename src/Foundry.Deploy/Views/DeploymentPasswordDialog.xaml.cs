// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;

namespace Foundry.Deploy.Views;

public partial class DeploymentPasswordDialog : Window
{
    private char[] password = [];

    public static readonly DependencyProperty PromptTextProperty = DependencyProperty.Register(
        nameof(PromptText), typeof(string), typeof(DeploymentPasswordDialog), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty PasswordPlaceholderProperty = DependencyProperty.Register(
        nameof(PasswordPlaceholder), typeof(string), typeof(DeploymentPasswordDialog), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty UnlockTextProperty = DependencyProperty.Register(
        nameof(UnlockText), typeof(string), typeof(DeploymentPasswordDialog), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty CancelTextProperty = DependencyProperty.Register(
        nameof(CancelText), typeof(string), typeof(DeploymentPasswordDialog), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ErrorTextProperty = DependencyProperty.Register(
        nameof(ErrorText), typeof(string), typeof(DeploymentPasswordDialog), new PropertyMetadata(string.Empty));

    public DeploymentPasswordDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordInput.Focus();
        Closed += (_, _) => PasswordInput.Password = string.Empty;
    }

    public string PromptText { get => (string)GetValue(PromptTextProperty); set => SetValue(PromptTextProperty, value); }

    public string PasswordPlaceholder { get => (string)GetValue(PasswordPlaceholderProperty); set => SetValue(PasswordPlaceholderProperty, value); }

    public string UnlockText { get => (string)GetValue(UnlockTextProperty); set => SetValue(UnlockTextProperty, value); }

    public string CancelText { get => (string)GetValue(CancelTextProperty); set => SetValue(CancelTextProperty, value); }

    public string ErrorText { get => (string)GetValue(ErrorTextProperty); set => SetValue(ErrorTextProperty, value); }

    public char[] TakePassword()
    {
        char[] value = password;
        password = [];
        return value;
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        password = PasswordInput.Password.ToCharArray();
        PasswordInput.Password = string.Empty;
        DialogResult = true;
    }
}
