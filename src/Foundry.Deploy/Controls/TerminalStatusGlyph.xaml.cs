// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Effects;
using Foundry.Deploy.Motion;

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
        ForegroundGlyph.SetResourceReference(TextBlock.ForegroundProperty, IsSuccess
            ? "SystemFillColorSuccessBrush"
            : "SystemFillColorCriticalBrush");
        ForegroundGlyph.Text = Glyph;
        if (SystemParameters.HighContrast)
        {
            ForegroundGlyph.Effect = null;
            return;
        }

        var effect = new DropShadowEffect
        {
            BlurRadius = 16,
            Opacity = 1,
            ShadowDepth = 0
        };
        BindingOperations.SetBinding(effect, DropShadowEffect.ColorProperty, new Binding("Foreground.Color")
        {
            Source = ForegroundGlyph,
            Mode = BindingMode.OneWay
        });
        ForegroundGlyph.Effect = effect;
    }

    private void PlayEntranceAnimation()
    {
        TransitionAnimator.FadeAndScale(GlyphHost, GlyphScale, 0.94, 0.45);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        TransitionAnimator.Clear(GlyphHost, GlyphScale);
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
    }
}
