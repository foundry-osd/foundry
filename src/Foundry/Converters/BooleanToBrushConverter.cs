using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Foundry.Converters;

/// <summary>
/// Converts a Boolean value into one of two configured brushes for XAML bindings.
/// </summary>
public sealed partial class BooleanToBrushConverter : DependencyObject, IValueConverter
{
    /// <summary>
    /// Identifies the <see cref="TrueBrush"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty TrueBrushProperty = DependencyProperty.Register(
        nameof(TrueBrush),
        typeof(Brush),
        typeof(BooleanToBrushConverter),
        new PropertyMetadata(default(Brush)));

    /// <summary>
    /// Identifies the <see cref="FalseBrush"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty FalseBrushProperty = DependencyProperty.Register(
        nameof(FalseBrush),
        typeof(Brush),
        typeof(BooleanToBrushConverter),
        new PropertyMetadata(default(Brush)));

    /// <summary>
    /// Gets or sets the brush returned for <see langword="true"/> values.
    /// </summary>
    public Brush? TrueBrush
    {
        get => (Brush?)GetValue(TrueBrushProperty);
        set => SetValue(TrueBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush returned for <see langword="false"/> values.
    /// </summary>
    public Brush? FalseBrush
    {
        get => (Brush?)GetValue(FalseBrushProperty);
        set => SetValue(FalseBrushProperty, value);
    }

    /// <inheritdoc />
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? TrueBrush : FalseBrush;
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
