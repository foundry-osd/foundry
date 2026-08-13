// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Controls;

/// <summary>
/// Hosts horizontally scrollable content with viewport navigation controls.
/// </summary>
public sealed partial class HorizontalScrollContainer : UserControl
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source),
        typeof(object),
        typeof(HorizontalScrollContainer),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ScrollBackTextProperty = DependencyProperty.Register(
        nameof(ScrollBackText),
        typeof(string),
        typeof(HorizontalScrollContainer),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ScrollForwardTextProperty = DependencyProperty.Register(
        nameof(ScrollForwardText),
        typeof(string),
        typeof(HorizontalScrollContainer),
        new PropertyMetadata(string.Empty));

    /// <summary>
    /// Initializes a new instance of the <see cref="HorizontalScrollContainer"/> class.
    /// </summary>
    public HorizontalScrollContainer()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets or sets the content displayed inside the horizontal viewport.
    /// </summary>
    public object? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the localized label for the back scroll button.
    /// </summary>
    public string ScrollBackText
    {
        get => (string)GetValue(ScrollBackTextProperty);
        set => SetValue(ScrollBackTextProperty, value);
    }

    /// <summary>
    /// Gets or sets the localized label for the forward scroll button.
    /// </summary>
    public string ScrollForwardText
    {
        get => (string)GetValue(ScrollForwardTextProperty);
        set => SetValue(ScrollForwardTextProperty, value);
    }

    private void Scroller_ViewChanging(object sender, ScrollViewerViewChangingEventArgs e)
    {
        ScrollBackButton.Visibility = e.FinalView.HorizontalOffset < 1
            ? Visibility.Collapsed
            : Visibility.Visible;
        ScrollForwardButton.Visibility = e.FinalView.HorizontalOffset > Scroller.ScrollableWidth - 1
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ScrollBackButton_Click(object sender, RoutedEventArgs e)
    {
        Scroller.ChangeView(Scroller.HorizontalOffset - Scroller.ViewportWidth, null, null);
        ScrollForwardButton.Focus(FocusState.Programmatic);
    }

    private void ScrollForwardButton_Click(object sender, RoutedEventArgs e)
    {
        Scroller.ChangeView(Scroller.HorizontalOffset + Scroller.ViewportWidth, null, null);
        ScrollBackButton.Focus(FocusState.Programmatic);
    }

    private void Scroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateButtonsVisibility();
    }

    private void ContentPresenter_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateButtonsVisibility();
    }

    private void UpdateButtonsVisibility()
    {
        ScrollBackButton.Visibility = Scroller.HorizontalOffset < 1
            ? Visibility.Collapsed
            : Visibility.Visible;
        ScrollForwardButton.Visibility = Scroller.ScrollableWidth > 0
            && Scroller.HorizontalOffset < Scroller.ScrollableWidth - 1
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
