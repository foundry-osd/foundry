// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Foundry.Avalonia.Controls;
using Foundry.Avalonia.Services.Motion;

namespace Foundry.Avalonia.Tests.Controls;

public sealed class SharedControlTests
{
    [AvaloniaFact]
    public void SharedControlThemesAndIconsResolveFromTheFoundryTheme()
    {
        Application application = Assert.IsType<TestApp>(Application.Current);

        foreach (Type controlType in new[]
                 {
                     typeof(FoundryShell),
                     typeof(AppUtilityStrip),
                     typeof(StatusIndicator),
                     typeof(InformationFieldGrid),
                     typeof(FoundryProgressRing),
                 })
        {
            Assert.True(application.TryGetResource(controlType, ThemeVariant.Default, out object? theme));
            Assert.IsType<ControlTheme>(theme);
        }

        foreach (string iconKey in new[]
                 {
                     "FoundryIcon.Warning",
                     "FoundryIcon.Success",
                     "FoundryIcon.Critical",
                     "FoundryIcon.Refresh",
                 })
        {
            Assert.True(application.TryGetResource(iconKey, ThemeVariant.Default, out object? icon));
            Assert.IsType<StreamGeometry>(icon);
        }
    }

    [AvaloniaFact]
    public void ShellAndUtilityStripExposeOnlyGenericContentSlots()
    {
        object brand = new();
        object menu = new();
        object trailing = new();
        object content = new();
        var shell = new FoundryShell
        {
            BrandContent = brand,
            MenuContent = menu,
            TrailingStatusContent = trailing,
            Content = content,
        };
        var utilityStrip = new AppUtilityStrip
        {
            MenuContent = menu,
            TrailingContent = trailing,
        };

        Assert.Same(brand, shell.BrandContent);
        Assert.Same(menu, shell.MenuContent);
        Assert.Same(trailing, shell.TrailingStatusContent);
        Assert.Same(content, shell.Content);
        Assert.Same(menu, utilityStrip.MenuContent);
        Assert.Same(trailing, utilityStrip.TrailingContent);
    }

    [AvaloniaFact]
    public void OptionalShellAndUtilitySlotsCollapseWhenEmpty()
    {
        var shell = new FoundryShell();
        var window = new Window { Width = 1280, Height = 768, Content = shell };
        window.Show();
        try
        {
            Border brandHeader = FindVisual<Border>(shell, "PART_BrandHeader");
            AppUtilityStrip utilityStrip = FindVisual<AppUtilityStrip>(shell, "PART_UtilityStrip");
            utilityStrip.ApplyTemplate();
            ContentPresenter menuPresenter = FindVisual<ContentPresenter>(utilityStrip, "PART_MenuContent");
            ContentPresenter trailingPresenter = FindVisual<ContentPresenter>(utilityStrip, "PART_TrailingContent");

            Assert.False(brandHeader.IsVisible);
            Assert.False(utilityStrip.IsVisible);

            shell.BrandContent = new TextBlock { Text = "Brand" };
            shell.MenuContent = new Button { Content = "Menu" };
            shell.TrailingStatusContent = new TextBlock { Text = "Version" };

            Assert.True(brandHeader.IsVisible);
            Assert.True(utilityStrip.IsVisible);
            Assert.True(menuPresenter.IsVisible);
            Assert.True(trailingPresenter.IsVisible);
            ContentPresenter mainContent = FindVisual<ContentPresenter>(shell, "PART_MainContent");
            Assert.Empty(mainContent.GetVisualAncestors().OfType<ScrollViewer>());

            shell.BrandContent = null;
            shell.MenuContent = null;
            shell.TrailingStatusContent = null;

            Assert.False(brandHeader.IsVisible);
            Assert.False(utilityStrip.IsVisible);
            Assert.False(menuPresenter.IsVisible);
            Assert.False(trailingPresenter.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void UtilityStripMirrorsLeadingAndTrailingSlotsForRightToLeftLayout()
    {
        (double leftToRightMenu, double leftToRightTrailing) = GetUtilitySlotPositions(FlowDirection.LeftToRight);
        (double rightToLeftMenu, double rightToLeftTrailing) = GetUtilitySlotPositions(FlowDirection.RightToLeft);

        Assert.True(
            leftToRightMenu < leftToRightTrailing,
            $"LTR menu {leftToRightMenu}, trailing {leftToRightTrailing}.");
        Assert.True(
            rightToLeftMenu > rightToLeftTrailing,
            $"RTL menu {rightToLeftMenu}, trailing {rightToLeftTrailing}.");
    }

    [AvaloniaTheory]
    [InlineData(StatusIndicatorKind.Neutral, ":neutral")]
    [InlineData(StatusIndicatorKind.Success, ":success")]
    [InlineData(StatusIndicatorKind.Caution, ":caution")]
    [InlineData(StatusIndicatorKind.Critical, ":critical")]
    public void StatusKindSelectsOneSemanticPseudoClass(
        StatusIndicatorKind kind,
        string expectedPseudoClass)
    {
        var indicator = new TestStatusIndicator { Kind = kind };

        Assert.True(indicator.HasPseudoClass(expectedPseudoClass));
        Assert.Equal(1, indicator.ActiveKindPseudoClassCount);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(indicator));
    }

    [AvaloniaFact]
    public void EmptyStatusSlotsCollapseAndSemanticStatesProvideDefaultIcons()
    {
        var indicator = new StatusIndicator { Title = "Status" };
        var window = new Window { Content = indicator };
        window.Show();
        try
        {
            Grid iconHost = FindVisual<Grid>(indicator, "PART_IconHost");
            ContentPresenter accessory = FindVisual<ContentPresenter>(indicator, "PART_Accessory");

            Assert.False(iconHost.IsVisible);
            Assert.False(accessory.IsVisible);

            indicator.Kind = StatusIndicatorKind.Critical;
            indicator.Content = new Button { Content = "Action" };

            Assert.True(iconHost.IsVisible);
            Assert.True(accessory.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void StatusTemplateUsesSemanticBrushesAndKeepsAccessoryContentFocusable()
    {
        Application application = Assert.IsType<TestApp>(Application.Current);
        Assert.True(application.TryGetResource(
            "FoundryBrush.Success",
            ThemeVariant.Light,
            out object? successBrush));
        var accessory = new Button { Content = "Action" };
        var indicator = new StatusIndicator
        {
            Kind = StatusIndicatorKind.Success,
            Title = "Ready",
            Description = "All requirements are satisfied.",
            Content = accessory,
        };
        var window = new Window { Content = indicator };
        window.Show();
        try
        {
            TextBlock title = FindVisual<TextBlock>(indicator, "PART_Title");
            TextBlock description = FindVisual<TextBlock>(indicator, "PART_Description");

            Assert.Same(successBrush, indicator.Foreground);
            Assert.Equal(TextWrapping.Wrap, title.TextWrapping);
            Assert.Equal(TextWrapping.Wrap, description.TextWrapping);
            Assert.True(accessory.Focusable);
            Assert.Equal("Ready", AutomationProperties.GetName(indicator));
            Assert.Equal("All requirements are satisfied.", AutomationProperties.GetHelpText(indicator));

            AutomationProperties.SetName(indicator, "Custom status name");
            indicator.Title = "Updated";

            Assert.Equal("Custom status name", AutomationProperties.GetName(indicator));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InformationGridKeepsApplicationOwnedItemsAndTemplates()
    {
        object[] items = [new(), new()];
        IDataTemplate template = new FuncDataTemplate<object>((_, _) => new TextBlock());
        var grid = new InformationFieldGrid
        {
            ItemsSource = items,
            ItemTemplate = template,
            ColumnCount = 2,
        };

        Assert.Same(items, grid.ItemsSource);
        Assert.Same(template, grid.ItemTemplate);
        Assert.Equal(2, grid.ColumnCount);
        Assert.Throws<ArgumentException>(() => grid.ColumnCount = 0);
    }

    [AvaloniaFact]
    public void InformationGridAppliesItsColumnCountToTheItemsPanel()
    {
        var grid = new InformationFieldGrid
        {
            ItemsSource = new[] { "One", "Two", "Three", "Four", "Five" },
            ItemTemplate = new FuncDataTemplate<string>((value, _) => new TextBlock { Text = value }),
            ColumnCount = 2,
        };
        var window = new Window { Content = grid };
        window.Show();
        try
        {
            UniformGrid panel = Assert.Single(grid.GetVisualDescendants().OfType<UniformGrid>());

            Assert.Equal(2, panel.Columns);
            Assert.Equal(5, panel.Children.Count);

            grid.ColumnCount = 3;

            Assert.Equal(3, panel.Columns);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ProgressRingProjectsRangeStateAndAccessibility()
    {
        var progress = new FoundryProgressRing
        {
            Minimum = 50,
            Maximum = 250,
            Value = 150,
            AccessibleLabel = "Deployment progress",
            StrokeThickness = 10,
        };
        var window = new Window { Content = progress };
        window.Show();
        try
        {
            Assert.Equal(50, progress.Percentage);
            Assert.Equal(180, progress.SweepAngle);
            Assert.Equal("Deployment progress", AutomationProperties.GetName(progress));
            Assert.Equal(10, progress.StrokeThickness);
            Arc indicator = FindVisual<Arc>(progress, "PART_RingIndicator");
            Assert.Equal(180, indicator.SweepAngle);

            progress.Value = 500;

            Assert.Equal(250, progress.Value);
            Assert.Equal(360, progress.SweepAngle);

            progress.IsIndeterminate = true;

            Assert.Equal(90, progress.SweepAngle);
            Assert.Throws<ArgumentException>(() => progress.StrokeThickness = 0);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ProgressRingOnlyEnablesContinuousMotionForFullMode()
    {
        var progress = new TestFoundryProgressRing { MotionMode = FoundryMotionMode.Reduced };

        Assert.False(progress.HasPseudoClass(":motion-full"));

        progress.MotionMode = FoundryMotionMode.Full;

        Assert.True(progress.HasPseudoClass(":motion-full"));

        progress.MotionMode = FoundryMotionMode.None;

        Assert.False(progress.HasPseudoClass(":motion-full"));
    }

    [AvaloniaFact]
    public void ReadOnlyTextDialogPresentsSelectableTextAndClosesFromItsButton()
    {
        var dialog = new ReadOnlyTextDialog
        {
            Title = "Diagnostics",
            Text = "Diagnostic details",
            CloseButtonContent = "Close",
            ActionContent = new Button(),
        };
        dialog.Show();
        try
        {
            TextBox textBox = Assert.Single(
                dialog.GetVisualDescendants().OfType<TextBox>(),
                control => control.Name == "PART_Text");
            Button closeButton = Assert.Single(
                dialog.GetVisualDescendants().OfType<Button>(),
                control => control.Name == "PART_CloseButton");

            Assert.True(textBox.IsReadOnly);
            Assert.True(textBox.AcceptsReturn);
            Assert.True(textBox.IsFocused);
            Assert.Equal("Diagnostic details", textBox.Text);
            Assert.NotNull(dialog.ActionContent);

            closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.False(dialog.IsVisible);
        }
        finally
        {
            if (dialog.IsVisible)
            {
                dialog.Close();
            }
        }
    }

    [AvaloniaFact]
    public void ReadOnlyTextDialogClosesOnEscape()
    {
        var dialog = new ReadOnlyTextDialog { Text = "Details" };
        dialog.Show();

        dialog.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Escape,
        });

        Assert.False(dialog.IsVisible);
    }

    [AvaloniaFact]
    public async Task ReadOnlyTextDialogSupportsAnOwnedModalLifetime()
    {
        var ownerContent = new Button { Content = "Owner" };
        var owner = new Window { Content = ownerContent };
        owner.Show();
        ownerContent.Focus();
        var dialog = new ReadOnlyTextDialog
        {
            Text = "Details",
            CloseButtonContent = "Close",
        };
        try
        {
            Task dialogLifetime = dialog.ShowDialog(owner);

            Assert.Same(owner, dialog.Owner);
            Assert.True(dialog.IsVisible);
            Assert.True(FindVisual<TextBox>(dialog, "PART_Text").IsFocused);

            dialog.Close();
            await dialogLifetime;

            Assert.False(dialog.IsVisible);
            Assert.True(owner.IsVisible);
            Assert.True(ownerContent.IsFocused);
        }
        finally
        {
            if (dialog.IsVisible)
            {
                dialog.Close();
            }

            owner.Close();
        }
    }

    private sealed class TestStatusIndicator : StatusIndicator
    {
        public bool HasPseudoClass(string pseudoClass) => PseudoClasses.Contains(pseudoClass);

        public int ActiveKindPseudoClassCount =>
            new[] { ":neutral", ":success", ":caution", ":critical" }.Count(PseudoClasses.Contains);
    }

    private sealed class TestFoundryProgressRing : FoundryProgressRing
    {
        public bool HasPseudoClass(string pseudoClass) => PseudoClasses.Contains(pseudoClass);
    }

    private static T FindVisual<T>(Visual root, string name)
        where T : Visual
    {
        return Assert.Single(
            root.GetVisualDescendants().OfType<T>(),
            visual => visual.Name == name);
    }

    private static (double Menu, double Trailing) GetUtilitySlotPositions(FlowDirection flowDirection)
    {
        var strip = new AppUtilityStrip
        {
            Width = 500,
            FlowDirection = flowDirection,
            MenuContent = new Border { Width = 100 },
            TrailingContent = new Border { Width = 80 },
        };
        var window = new Window { Width = 500, Content = strip };
        window.Show();
        try
        {
            ContentPresenter menu = FindVisual<ContentPresenter>(strip, "PART_MenuContent");
            ContentPresenter trailing = FindVisual<ContentPresenter>(strip, "PART_TrailingContent");
            Point menuCenter = menu.TranslatePoint(
                new Point(menu.Bounds.Width / 2, menu.Bounds.Height / 2),
                window) ?? throw new InvalidOperationException("Menu presenter is not attached.");
            Point trailingCenter = trailing.TranslatePoint(
                new Point(trailing.Bounds.Width / 2, trailing.Bounds.Height / 2),
                window) ?? throw new InvalidOperationException("Trailing presenter is not attached.");
            return (menuCenter.X, trailingCenter.X);
        }
        finally
        {
            window.Close();
        }
    }
}
