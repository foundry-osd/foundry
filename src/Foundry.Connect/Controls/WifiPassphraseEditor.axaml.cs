// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Foundry.Connect.Controls;

public partial class WifiPassphraseEditor : UserControl
{
    public static readonly StyledProperty<string> PasswordProperty =
        AvaloniaProperty.Register<WifiPassphraseEditor, string>(nameof(Password), string.Empty, defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsRevealedProperty =
        AvaloniaProperty.Register<WifiPassphraseEditor, bool>(nameof(IsRevealed), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<WifiPassphraseEditor, bool>(nameof(IsActive));

    public static readonly StyledProperty<ICommand?> SubmitCommandProperty =
        AvaloniaProperty.Register<WifiPassphraseEditor, ICommand?>(nameof(SubmitCommand));

    public static readonly StyledProperty<string?> AccessibleRevealLabelProperty =
        AvaloniaProperty.Register<WifiPassphraseEditor, string?>(nameof(AccessibleRevealLabel));

    private TextBox? _editor;

    public WifiPassphraseEditor()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public string Password
    {
        get => GetValue(PasswordProperty);
        set => SetValue(PasswordProperty, value);
    }

    public bool IsRevealed
    {
        get => GetValue(IsRevealedProperty);
        set => SetValue(IsRevealedProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public ICommand? SubmitCommand
    {
        get => GetValue(SubmitCommandProperty);
        set => SetValue(SubmitCommandProperty, value);
    }

    public string? AccessibleRevealLabel
    {
        get => GetValue(AccessibleRevealLabelProperty);
        set => SetValue(AccessibleRevealLabelProperty, value);
    }

    public void FocusEditor() => _editor?.Focus();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsRevealedProperty)
        {
            UpdatePasswordPresentation(change.GetNewValue<bool>());
        }
        else if (change.Property == IsActiveProperty && !change.GetNewValue<bool>())
        {
            IsRevealed = false;
        }
    }

    private void OnEditorAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _editor = (TextBox)sender!;
        UpdatePasswordPresentation(IsRevealed);
    }

    private void OnRevealClicked(object? sender, RoutedEventArgs e)
    {
        IsRevealed = !IsRevealed;
        UpdatePasswordPresentation(IsRevealed);
        FocusEditor();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || SubmitCommand?.CanExecute(null) != true)
        {
            return;
        }

        SubmitCommand.Execute(null);
        e.Handled = true;
    }

    private void UpdatePasswordPresentation(bool isRevealed)
    {
        if (_editor is not null)
        {
            _editor.RevealPassword = isRevealed;
        }
    }
}
