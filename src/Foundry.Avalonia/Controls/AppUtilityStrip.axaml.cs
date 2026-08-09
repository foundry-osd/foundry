// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;

namespace Foundry.Avalonia.Controls;

[PseudoClasses(":has-menu", ":has-trailing")]
public class AppUtilityStrip : TemplatedControl
{
    public static readonly StyledProperty<object?> MenuContentProperty =
        AvaloniaProperty.Register<AppUtilityStrip, object?>(nameof(MenuContent));

    public static readonly StyledProperty<object?> TrailingContentProperty =
        AvaloniaProperty.Register<AppUtilityStrip, object?>(nameof(TrailingContent));

    public AppUtilityStrip()
    {
        UpdateSlotPseudoClasses();
    }

    public object? MenuContent
    {
        get => GetValue(MenuContentProperty);
        set => SetValue(MenuContentProperty, value);
    }

    public object? TrailingContent
    {
        get => GetValue(TrailingContentProperty);
        set => SetValue(TrailingContentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MenuContentProperty || change.Property == TrailingContentProperty)
        {
            UpdateSlotPseudoClasses();
        }

    }

    private void UpdateSlotPseudoClasses()
    {
        PseudoClasses.Set(":has-menu", MenuContent is not null);
        PseudoClasses.Set(":has-trailing", TrailingContent is not null);
    }
}
