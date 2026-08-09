// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;

namespace Foundry.Avalonia.Controls;

[PseudoClasses(":has-brand", ":has-utility")]
public class FoundryShell : ContentControl
{
    public static readonly StyledProperty<object?> BrandContentProperty =
        AvaloniaProperty.Register<FoundryShell, object?>(nameof(BrandContent));

    public static readonly StyledProperty<object?> MenuContentProperty =
        AvaloniaProperty.Register<FoundryShell, object?>(nameof(MenuContent));

    public static readonly StyledProperty<object?> TrailingStatusContentProperty =
        AvaloniaProperty.Register<FoundryShell, object?>(nameof(TrailingStatusContent));

    public FoundryShell()
    {
        UpdateSlotPseudoClasses();
    }

    public object? BrandContent
    {
        get => GetValue(BrandContentProperty);
        set => SetValue(BrandContentProperty, value);
    }

    public object? MenuContent
    {
        get => GetValue(MenuContentProperty);
        set => SetValue(MenuContentProperty, value);
    }

    public object? TrailingStatusContent
    {
        get => GetValue(TrailingStatusContentProperty);
        set => SetValue(TrailingStatusContentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BrandContentProperty ||
            change.Property == MenuContentProperty ||
            change.Property == TrailingStatusContentProperty)
        {
            UpdateSlotPseudoClasses();
        }
    }

    private void UpdateSlotPseudoClasses()
    {
        PseudoClasses.Set(":has-brand", BrandContent is not null);
        PseudoClasses.Set(":has-utility", MenuContent is not null || TrailingStatusContent is not null);
    }
}
