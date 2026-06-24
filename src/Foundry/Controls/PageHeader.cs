// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Microsoft.UI.Xaml.Media;

namespace Foundry.Controls;

public sealed partial class PageHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(PageHeader),
        new PropertyMetadata(string.Empty, OnHeaderPropertyChanged));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(PageHeader),
        new PropertyMetadata(string.Empty, OnHeaderPropertyChanged));

    public static readonly DependencyProperty IconGlyphProperty = DependencyProperty.Register(
        nameof(IconGlyph),
        typeof(string),
        typeof(PageHeader),
        new PropertyMetadata(string.Empty, OnHeaderPropertyChanged));

    public static readonly DependencyProperty DocumentationUrlProperty = DependencyProperty.Register(
        nameof(DocumentationUrl),
        typeof(string),
        typeof(PageHeader),
        new PropertyMetadata(string.Empty, OnHeaderPropertyChanged));

    private readonly FontIcon icon = new();
    private readonly TextBlock titleTextBlock = new();
    private readonly TextBlock descriptionTextBlock = new();
    private readonly DocumentationButton documentationButton = new();

    public PageHeader()
    {
        Content = CreateLayout();
        UpdateContent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string IconGlyph
    {
        get => (string)GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }

    public string DocumentationUrl
    {
        get => (string)GetValue(DocumentationUrlProperty);
        set => SetValue(DocumentationUrlProperty, value);
    }

    private static void OnHeaderPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        ((PageHeader)dependencyObject).UpdateContent();
    }

    private Grid CreateLayout()
    {
        Grid layout = new()
        {
            Margin = (Thickness)Application.Current.Resources["FoundryPageHeaderMargin"],
            ColumnSpacing = (double)Application.Current.Resources["FoundryPageHeaderColumnSpacing"]
        };

        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        icon.Width = (double)Application.Current.Resources["FoundryIconSizeHero"];
        icon.Height = (double)Application.Current.Resources["FoundryIconSizeHero"];
        icon.FontSize = (double)Application.Current.Resources["FoundryIconSizeLarge"];
        icon.VerticalAlignment = VerticalAlignment.Center;

        StackPanel textPanel = new()
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = (double)Application.Current.Resources["FoundryTextGroupSpacing"]
        };

        titleTextBlock.HorizontalAlignment = HorizontalAlignment.Left;
        titleTextBlock.Style = (Style)Application.Current.Resources["FoundryPageTitleTextBlockStyle"];

        descriptionTextBlock.MaxWidth = (double)Application.Current.Resources["FoundrySummaryTextMaxWidth"];
        descriptionTextBlock.HorizontalAlignment = HorizontalAlignment.Left;
        descriptionTextBlock.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        descriptionTextBlock.TextAlignment = TextAlignment.Left;
        descriptionTextBlock.TextWrapping = TextWrapping.WrapWholeWords;
        descriptionTextBlock.Style = (Style)Application.Current.Resources["FoundryBodyTextBlockStyle"];

        textPanel.Children.Add(titleTextBlock);
        textPanel.Children.Add(descriptionTextBlock);

        Grid.SetColumn(textPanel, 1);
        Grid.SetColumn(documentationButton, 2);
        documentationButton.HorizontalAlignment = HorizontalAlignment.Right;
        documentationButton.VerticalAlignment = VerticalAlignment.Center;

        layout.Children.Add(icon);
        layout.Children.Add(textPanel);
        layout.Children.Add(documentationButton);
        return layout;
    }

    private void UpdateContent()
    {
        icon.Glyph = IconGlyph;
        icon.Visibility = string.IsNullOrWhiteSpace(IconGlyph)
            ? Visibility.Collapsed
            : Visibility.Visible;
        titleTextBlock.Text = Title;
        descriptionTextBlock.Text = Description;
        documentationButton.DocumentationUrl = DocumentationUrl;
        documentationButton.Visibility = string.IsNullOrWhiteSpace(DocumentationUrl)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
