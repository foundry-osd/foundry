// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Controls;

/// <summary>
/// Displays a fixed-size action tile in the home landing page header.
/// </summary>
public sealed partial class HomeTile : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(HomeTile),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(HomeTile),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconGlyphProperty = DependencyProperty.Register(
        nameof(IconGlyph),
        typeof(string),
        typeof(HomeTile),
        new PropertyMetadata(string.Empty));

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeTile"/> class.
    /// </summary>
    public HomeTile()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Occurs when the tile is activated.
    /// </summary>
    public event RoutedEventHandler? Click;

    /// <summary>
    /// Gets or sets the tile title.
    /// </summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the tile description.
    /// </summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>
    /// Gets or sets the Segoe Fluent Icons glyph displayed by the tile.
    /// </summary>
    public string IconGlyph
    {
        get => (string)GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }

    private void TileButton_Click(object sender, RoutedEventArgs e)
    {
        Click?.Invoke(this, e);
    }
}
