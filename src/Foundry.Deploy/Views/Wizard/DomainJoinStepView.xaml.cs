// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Foundry.Deploy.ViewModels;

namespace Foundry.Deploy.Views.Wizard;

/// <summary>
/// Bridges the password editors, which cannot be data-bound, to the step's owned password buffer. The view is
/// rebuilt each time the technician returns to the step, so it restores the masked editor from that buffer when
/// it loads. The reveal button swaps the masked editor for a plain one, as the boot media password dialog does.
/// </summary>
public partial class DomainJoinStepView : UserControl
{
    private DomainJoinStepViewModel? step;
    private bool isPasswordRevealed;
    private bool isSynchronizingPasswordEditors;

    public DomainJoinStepView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        step = (DataContext as MainWindowViewModel)?.DomainJoinStep;
        if (step is null) return;
        step.PasswordCleared += OnPasswordCleared;
        RestorePassword(step);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (step is not null) step.PasswordCleared -= OnPasswordCleared;
        step = null;
        ClearPasswordEditors();
    }

    private void PasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (isSynchronizingPasswordEditors || step is null) return;
        using var secure = PasswordInput.SecurePassword;
        char[] characters = new char[secure.Length];
        IntPtr plaintext = IntPtr.Zero;
        try
        {
            plaintext = Marshal.SecureStringToGlobalAllocUnicode(secure);
            if (characters.Length > 0) Marshal.Copy(plaintext, characters, 0, characters.Length);
            step.SetPassword(characters);
        }
        finally
        {
            if (plaintext != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(plaintext);
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(characters.AsSpan()));
        }
    }

    private void PasswordRevealInput_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (isSynchronizingPasswordEditors || step is null) return;
        step.SetPassword(PasswordRevealInput.Text);
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

    private void OnPasswordCleared(object? sender, EventArgs e) => Dispatcher.Invoke(ClearPasswordEditors);

    private void RestorePassword(DomainJoinStepViewModel source)
    {
        char[]? password = source.GetPasswordCopy();
        if (password is null) return;
        try
        {
            isSynchronizingPasswordEditors = true;
            PasswordInput.Password = new string(password);
        }
        finally
        {
            isSynchronizingPasswordEditors = false;
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
        }
    }

    /// <summary>Empties both editors and returns to the masked one, without changing the step's owned password.</summary>
    private void ClearPasswordEditors()
    {
        isSynchronizingPasswordEditors = true;
        try
        {
            PasswordInput.Clear();
            PasswordRevealInput.Clear();
            PasswordRevealInput.Visibility = Visibility.Collapsed;
            PasswordInput.Visibility = Visibility.Visible;
            PasswordRevealButton.ClearValue(StyleProperty);
            isPasswordRevealed = false;
        }
        finally
        {
            isSynchronizingPasswordEditors = false;
        }
    }
}
