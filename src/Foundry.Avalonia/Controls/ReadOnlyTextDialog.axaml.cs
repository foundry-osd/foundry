// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Foundry.Avalonia.Controls;

public partial class ReadOnlyTextDialog : Window
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<ReadOnlyTextDialog, string?>(nameof(Text));

    public static readonly StyledProperty<object?> CloseButtonContentProperty =
        AvaloniaProperty.Register<ReadOnlyTextDialog, object?>(nameof(CloseButtonContent));

    public static readonly StyledProperty<object?> FooterContentProperty =
        AvaloniaProperty.Register<ReadOnlyTextDialog, object?>(nameof(FooterContent));

    public static readonly StyledProperty<object?> ActionContentProperty =
        AvaloniaProperty.Register<ReadOnlyTextDialog, object?>(nameof(ActionContent));

    public static readonly StyledProperty<TextWrapping> TextWrappingProperty =
        AvaloniaProperty.Register<ReadOnlyTextDialog, TextWrapping>(
            nameof(TextWrapping),
            defaultValue: TextWrapping.NoWrap);

    private Control? _ownerFocusTarget;

    public ReadOnlyTextDialog()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public object? CloseButtonContent
    {
        get => GetValue(CloseButtonContentProperty);
        set => SetValue(CloseButtonContentProperty, value);
    }

    public object? FooterContent
    {
        get => GetValue(FooterContentProperty);
        set => SetValue(FooterContentProperty, value);
    }

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    public TextWrapping TextWrapping
    {
        get => GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    protected override void OnOpened(EventArgs eventArgs)
    {
        base.OnOpened(eventArgs);
        _ownerFocusTarget = Owner?.FocusManager?.GetFocusedElement() as Control;
        this.FindControl<TextBox>("PART_Text")?.Focus();
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        base.OnClosed(eventArgs);
        _ownerFocusTarget?.Focus();
        _ownerFocusTarget = null;
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape)
        {
            Close();
            eventArgs.Handled = true;
            return;
        }

        base.OnKeyDown(eventArgs);
    }

    private void CloseButtonOnClick(object? sender, RoutedEventArgs eventArgs) => Close();
}
