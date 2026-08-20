// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Microsoft.UI.Xaml.Media;

namespace Foundry.Controls;

/// <summary>
/// A compact, clickable card that shows an icon, title, coloured status badge,
/// and up to four label/value detail rows. Used on the Home landing page.
/// </summary>
public sealed partial class HomeStatusCard : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(HomeStatusCard), new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty IconGlyphProperty =
        DependencyProperty.Register(nameof(IconGlyph), typeof(string), typeof(HomeStatusCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(HomeStatusCard), new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty SeverityProperty =
        DependencyProperty.Register(nameof(Severity), typeof(InfoBarSeverity), typeof(HomeStatusCard), new PropertyMetadata(InfoBarSeverity.Informational, OnSeverityChanged));

    public static readonly DependencyProperty Line1LabelProperty =
        DependencyProperty.Register(nameof(Line1Label), typeof(string), typeof(HomeStatusCard), new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty Line1ValueProperty =
        DependencyProperty.Register(nameof(Line1Value), typeof(string), typeof(HomeStatusCard), new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty Line2LabelProperty =
        DependencyProperty.Register(nameof(Line2Label), typeof(string), typeof(HomeStatusCard), new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty Line2ValueProperty =
        DependencyProperty.Register(nameof(Line2Value), typeof(string), typeof(HomeStatusCard), new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty Line3LabelProperty =
        DependencyProperty.Register(nameof(Line3Label), typeof(string), typeof(HomeStatusCard), new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty Line3ValueProperty =
        DependencyProperty.Register(nameof(Line3Value), typeof(string), typeof(HomeStatusCard), new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty Line4LabelProperty =
        DependencyProperty.Register(nameof(Line4Label), typeof(string), typeof(HomeStatusCard), new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty Line4ValueProperty =
        DependencyProperty.Register(nameof(Line4Value), typeof(string), typeof(HomeStatusCard), new PropertyMetadata(string.Empty, OnContentChanged));

    /// <summary>Raised when the card is clicked.</summary>
    public event EventHandler? NavigationRequested;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeStatusCard"/> class.
    /// </summary>
    public HomeStatusCard()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string IconGlyph
    {
        get => (string)GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public InfoBarSeverity Severity
    {
        get => (InfoBarSeverity)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public string Line1Label
    {
        get => (string)GetValue(Line1LabelProperty);
        set => SetValue(Line1LabelProperty, value);
    }

    public string Line1Value
    {
        get => (string)GetValue(Line1ValueProperty);
        set => SetValue(Line1ValueProperty, value);
    }

    public string Line2Label
    {
        get => (string)GetValue(Line2LabelProperty);
        set => SetValue(Line2LabelProperty, value);
    }

    public string Line2Value
    {
        get => (string)GetValue(Line2ValueProperty);
        set => SetValue(Line2ValueProperty, value);
    }

    public string Line3Label
    {
        get => (string)GetValue(Line3LabelProperty);
        set => SetValue(Line3LabelProperty, value);
    }

    public string Line3Value
    {
        get => (string)GetValue(Line3ValueProperty);
        set => SetValue(Line3ValueProperty, value);
    }

    public string Line4Label
    {
        get => (string)GetValue(Line4LabelProperty);
        set => SetValue(Line4LabelProperty, value);
    }

    public string Line4Value
    {
        get => (string)GetValue(Line4ValueProperty);
        set => SetValue(Line4ValueProperty, value);
    }

    /// <summary>Background brush of the status badge, computed from <see cref="Severity"/>.</summary>
    public Brush BadgeBackground => Severity switch
    {
        InfoBarSeverity.Success => (Brush)Application.Current.Resources["FoundryStatusReadyBrush"],
        InfoBarSeverity.Error => (Brush)Application.Current.Resources["FoundryStatusBlockedBrush"],
        _ => (Brush)Application.Current.Resources["FoundryStatusNeutralBrush"],
    };

    /// <summary>Foreground brush of the status badge, computed from <see cref="Severity"/>.</summary>
    public Brush BadgeForeground => Severity switch
    {
        InfoBarSeverity.Success => (Brush)Application.Current.Resources["FoundryStatusReadyForegroundBrush"],
        InfoBarSeverity.Error => (Brush)Application.Current.Resources["FoundryStatusBlockedForegroundBrush"],
        _ => (Brush)Application.Current.Resources["FoundryStatusNeutralForegroundBrush"],
    };

    /// <summary>Glyph shown in the badge, computed from <see cref="Severity"/>.</summary>
    public string BadgeGlyph => Severity switch
    {
        InfoBarSeverity.Success => "\uE73E",
        InfoBarSeverity.Error => "\uE711",
        _ => "\uE9CE",
    };

    public Visibility Line1Visibility => string.IsNullOrEmpty(Line1Label) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility Line2Visibility => string.IsNullOrEmpty(Line2Label) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility Line3Visibility => string.IsNullOrEmpty(Line3Label) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility Line4Visibility => string.IsNullOrEmpty(Line4Label) ? Visibility.Collapsed : Visibility.Visible;

    public string AutomationName
    {
        get
        {
            List<string> parts = [Title, StatusText];
            AddDetail(parts, Line1Label, Line1Value);
            AddDetail(parts, Line2Label, Line2Value);
            AddDetail(parts, Line3Label, Line3Value);
            AddDetail(parts, Line4Label, Line4Value);
            return string.Join(". ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }
    }

    private static void OnSeverityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HomeStatusCard card)
        {
            card.Bindings.Update();
        }
    }

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HomeStatusCard card)
        {
            card.Bindings.Update();
        }
    }

    private static void AddDetail(List<string> parts, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(label))
        {
            parts.Add($"{label}: {value}");
        }
    }

    private void CardButton_Click(object sender, RoutedEventArgs e)
    {
        NavigationRequested?.Invoke(this, EventArgs.Empty);
    }
}
