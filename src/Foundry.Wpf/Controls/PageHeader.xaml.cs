// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Controls;

namespace Foundry.Wpf.Controls;

/// <summary>
/// Shared page heading made of an icon glyph, a title and a description, styled consistently across
/// WPF applications. Purely presentational: all values are set through its dependency properties.
/// </summary>
public partial class PageHeader : UserControl
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph),
        typeof(string),
        typeof(PageHeader),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(PageHeader),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(PageHeader),
        new PropertyMetadata(string.Empty));

    public PageHeader()
    {
        InitializeComponent();
    }

    /// <summary>Icon glyph shown before the title (a character from the application icon font).</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Heading text.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Secondary text shown under the title.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
