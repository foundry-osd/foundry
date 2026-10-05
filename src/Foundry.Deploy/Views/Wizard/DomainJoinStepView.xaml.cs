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
/// Bridges the password box, which cannot be data-bound, to the step's owned password buffer. The view is rebuilt
/// each time the technician returns to the step, so it restores the box from that buffer when it loads.
/// </summary>
public partial class DomainJoinStepView : UserControl
{
    private DomainJoinStepViewModel? step;
    private bool synchronizingPassword;

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
        ClearPasswordBox();
    }

    private void PasswordInput_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (synchronizingPassword || step is null) return;
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

    private void OnPasswordCleared(object? sender, EventArgs e) => Dispatcher.Invoke(ClearPasswordBox);

    private void RestorePassword(DomainJoinStepViewModel source)
    {
        char[]? password = source.GetPasswordCopy();
        if (password is null) return;
        try
        {
            synchronizingPassword = true;
            PasswordInput.Password = new string(password);
        }
        finally
        {
            synchronizingPassword = false;
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
        }
    }

    private void ClearPasswordBox()
    {
        synchronizingPassword = true;
        try { PasswordInput.Clear(); }
        finally { synchronizingPassword = false; }
    }
}
