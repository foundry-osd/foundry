// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Foundry.Wpf.Controls;

/// <summary>
/// Password editor with a reveal toggle. The password is never exposed as a bindable property: the owner copies it
/// with <see cref="GetPassphraseCopy"/> (and zeroes the copy) when <see cref="PassphraseChanged"/> fires. Enter is always
/// handled inside the editor so a window default button never fires from it; it executes <see cref="SubmitCommand"/> when
/// that command can execute.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PasswordBox.Password"/> is not a dependency property, so the control bridges the masked editor and a plain
/// <see cref="TextBox"/> used while the password is revealed, as the Deploy domain join step does.
/// </para>
/// <para>
/// Trade-off: WPF only accepts a <see cref="string"/> when a password is written into the masked editor
/// (<see cref="SetPassphrase"/>, hiding after a reveal), and the revealed editor holds the password as the text of a
/// <see cref="TextBox"/>. Those strings are immutable, cannot be zeroed and live until garbage collection. The array
/// returned by <see cref="GetPassphraseCopy"/> is the only copy the control hands out, and the caller wipes it.
/// </para>
/// </remarks>
public partial class PassphraseEditor : UserControl
{
    /// <summary>Identifies <see cref="AccessibleName"/>.</summary>
    public static readonly DependencyProperty AccessibleNameProperty = DependencyProperty.Register(
        nameof(AccessibleName),
        typeof(string),
        typeof(PassphraseEditor),
        new PropertyMetadata(string.Empty));

    /// <summary>Identifies <see cref="RevealButtonText"/>.</summary>
    public static readonly DependencyProperty RevealButtonTextProperty = DependencyProperty.Register(
        nameof(RevealButtonText),
        typeof(string),
        typeof(PassphraseEditor),
        new PropertyMetadata(string.Empty));

    /// <summary>Identifies <see cref="SubmitCommand"/>.</summary>
    public static readonly DependencyProperty SubmitCommandProperty = DependencyProperty.Register(
        nameof(SubmitCommand),
        typeof(ICommand),
        typeof(PassphraseEditor),
        new PropertyMetadata(null));

    /// <summary>Identifies <see cref="MaxLength"/>.</summary>
    public static readonly DependencyProperty MaxLengthProperty = DependencyProperty.Register(
        nameof(MaxLength),
        typeof(int),
        typeof(PassphraseEditor),
        new PropertyMetadata(256));

    private bool _isSynchronizing;

    public PassphraseEditor()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the technician edits the passphrase; never raised by <see cref="SetPassphrase"/> or <see cref="Clear"/>.</summary>
    public event EventHandler? PassphraseChanged;

    /// <summary>Gets or sets the accessible name of both inner editors.</summary>
    public string AccessibleName
    {
        get => (string)GetValue(AccessibleNameProperty);
        set => SetValue(AccessibleNameProperty, value);
    }

    /// <summary>Gets or sets the accessible name and the tooltip of the reveal toggle.</summary>
    public string RevealButtonText
    {
        get => (string)GetValue(RevealButtonTextProperty);
        set => SetValue(RevealButtonTextProperty, value);
    }

    /// <summary>Gets or sets the command executed on Enter when it can execute; Enter is handled either way.</summary>
    public ICommand? SubmitCommand
    {
        get => (ICommand?)GetValue(SubmitCommandProperty);
        set => SetValue(SubmitCommandProperty, value);
    }

    /// <summary>Gets or sets the maximum number of characters, 256 by default.</summary>
    public int MaxLength
    {
        get => (int)GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    /// <summary>Gets a value indicating whether the passphrase is currently shown in clear text.</summary>
    public bool IsRevealed { get; private set; }

    /// <summary>Returns a copy of the passphrase. The caller owns the array and must zero it once used.</summary>
    public char[] GetPassphraseCopy()
    {
        if (IsRevealed)
        {
            return RevealedInput.Text.ToCharArray();
        }

        using var secure = MaskedInput.SecurePassword;
        char[] characters = new char[secure.Length];
        if (characters.Length == 0)
        {
            return characters;
        }

        IntPtr plaintext = IntPtr.Zero;
        try
        {
            plaintext = Marshal.SecureStringToGlobalAllocUnicode(secure);
            Marshal.Copy(plaintext, characters, 0, characters.Length);
        }
        finally
        {
            if (plaintext != IntPtr.Zero)
            {
                Marshal.ZeroFreeGlobalAllocUnicode(plaintext);
            }
        }

        return characters;
    }

    /// <summary>Writes a passphrase into the visible editor without raising <see cref="PassphraseChanged"/>.</summary>
    public void SetPassphrase(ReadOnlySpan<char> value)
    {
        _isSynchronizing = true;
        try
        {
            if (IsRevealed)
            {
                RevealedInput.Text = new string(value);
            }
            else
            {
                MaskedInput.Password = new string(value);
            }
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    /// <summary>Empties both editors and returns to the masked one, without raising <see cref="PassphraseChanged"/>.</summary>
    public void Clear()
    {
        _isSynchronizing = true;
        try
        {
            MaskedInput.Clear();
            RevealedInput.Clear();
            ShowMasked();
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    /// <summary>Focuses the visible inner editor; a revealed passphrase gets the caret at its end.</summary>
    public void FocusEditor()
    {
        if (IsRevealed)
        {
            RevealedInput.Focus();
            RevealedInput.CaretIndex = RevealedInput.Text.Length;
        }
        else
        {
            MaskedInput.Focus();
        }
    }

    private void Root_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        // Handled for the whole editor, reveal toggle included: an unhandled Enter would reach the window's default button.
        e.Handled = true;
        ICommand? submit = SubmitCommand;
        if (submit?.CanExecute(null) == true)
        {
            submit.Execute(null);
        }
    }

    private void MaskedInput_OnPasswordChanged(object sender, RoutedEventArgs e) => RaisePassphraseChanged();

    private void RevealedInput_OnTextChanged(object sender, TextChangedEventArgs e) => RaisePassphraseChanged();

    private void RaisePassphraseChanged()
    {
        if (_isSynchronizing)
        {
            return;
        }

        PassphraseChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RevealButton_OnClick(object sender, RoutedEventArgs e)
    {
        _isSynchronizing = true;
        try
        {
            if (IsRevealed)
            {
                MaskedInput.Password = RevealedInput.Text;
                RevealedInput.Text = string.Empty;
                ShowMasked();
            }
            else
            {
                RevealedInput.Text = MaskedInput.Password;
                MaskedInput.Password = string.Empty;
                MaskedInput.Visibility = Visibility.Collapsed;
                RevealedInput.Visibility = Visibility.Visible;
                RevealButton.SetResourceReference(StyleProperty, "AccentButtonStyle");
                IsRevealed = true;
            }
        }
        finally
        {
            _isSynchronizing = false;
        }

        FocusEditor();
    }

    private void ShowMasked()
    {
        RevealedInput.Visibility = Visibility.Collapsed;
        MaskedInput.Visibility = Visibility.Visible;
        RevealButton.ClearValue(StyleProperty);
        IsRevealed = false;
    }
}
