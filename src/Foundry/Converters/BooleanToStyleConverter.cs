// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Microsoft.UI.Xaml.Data;

namespace Foundry.Converters;

/// <summary>
/// Converts a Boolean value into one of two configured styles for XAML bindings.
/// </summary>
public sealed partial class BooleanToStyleConverter : DependencyObject, IValueConverter
{
    /// <summary>
    /// Identifies the <see cref="TrueStyle"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty TrueStyleProperty = DependencyProperty.Register(
        nameof(TrueStyle),
        typeof(Style),
        typeof(BooleanToStyleConverter),
        new PropertyMetadata(default(Style)));

    /// <summary>
    /// Identifies the <see cref="FalseStyle"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty FalseStyleProperty = DependencyProperty.Register(
        nameof(FalseStyle),
        typeof(Style),
        typeof(BooleanToStyleConverter),
        new PropertyMetadata(default(Style)));

    /// <summary>
    /// Gets or sets the style returned for <see langword="true"/> values.
    /// </summary>
    public Style? TrueStyle
    {
        get => (Style?)GetValue(TrueStyleProperty);
        set => SetValue(TrueStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the style returned for <see langword="false"/> values.
    /// </summary>
    public Style? FalseStyle
    {
        get => (Style?)GetValue(FalseStyleProperty);
        set => SetValue(FalseStyleProperty, value);
    }

    /// <inheritdoc />
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? TrueStyle : FalseStyle;
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
