// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Foundry.Deploy.Controls;

public partial class TerminalStatusGlyph : UserControl
{
    public static readonly DependencyProperty IsSuccessProperty = DependencyProperty.Register(
        nameof(IsSuccess),
        typeof(bool),
        typeof(TerminalStatusGlyph),
        new PropertyMetadata(true, OnVisualPropertyChanged));

    public static readonly DependencyProperty AutomationNameProperty = DependencyProperty.Register(
        nameof(AutomationName),
        typeof(string),
        typeof(TerminalStatusGlyph),
        new PropertyMetadata(string.Empty));

    public TerminalStatusGlyph()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        IsVisibleChanged += OnIsVisibleChanged;
        Unloaded += OnUnloaded;
    }

    public bool IsSuccess
    {
        get => (bool)GetValue(IsSuccessProperty);
        set => SetValue(IsSuccessProperty, value);
    }

    public string AutomationName
    {
        get => (string)GetValue(AutomationNameProperty);
        set => SetValue(AutomationNameProperty, value);
    }

    public string Glyph => IsSuccess ? "\uE930" : "\uEA39";

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TerminalStatusGlyph glyph)
        {
            glyph.UpdateVisuals();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        UpdateVisuals();
        PlayEntranceAnimation();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && IsLoaded)
        {
            UpdateVisuals();
            PlayEntranceAnimation();
        }
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
        {
            UpdateVisuals();
        }
    }

    private void UpdateVisuals()
    {
        var brush = (SolidColorBrush)FindResource(IsSuccess
            ? "SystemFillColorSuccessBrush"
            : "SystemFillColorCriticalBrush");
        ForegroundGlyph.Foreground = brush;
        ForegroundGlyph.Text = Glyph;
        ForegroundGlyph.Effect = SystemParameters.HighContrast
            ? null
            : new DropShadowEffect
            {
                BlurRadius = 16,
                Color = brush.Color,
                Opacity = 1,
                ShadowDepth = 0
            };
    }

    private void PlayEntranceAnimation()
    {
        GlyphHost.BeginAnimation(OpacityProperty, null);
        GlyphScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        GlyphScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        var duration = TimeSpan.FromMilliseconds(220);
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        GlyphHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0.45, 1, duration), HandoffBehavior.SnapshotAndReplace);
        GlyphScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, duration) { EasingFunction = easing }, HandoffBehavior.SnapshotAndReplace);
        GlyphScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, duration) { EasingFunction = easing }, HandoffBehavior.SnapshotAndReplace);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        GlyphHost.BeginAnimation(OpacityProperty, null);
        GlyphScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        GlyphScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
    }
}
