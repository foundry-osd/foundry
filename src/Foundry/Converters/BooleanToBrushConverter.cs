using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Foundry.Converters;

public sealed partial class BooleanToBrushConverter : DependencyObject, IValueConverter
{
    public static readonly DependencyProperty TrueBrushProperty = DependencyProperty.Register(
        nameof(TrueBrush),
        typeof(Brush),
        typeof(BooleanToBrushConverter),
        new PropertyMetadata(default(Brush)));

    public static readonly DependencyProperty FalseBrushProperty = DependencyProperty.Register(
        nameof(FalseBrush),
        typeof(Brush),
        typeof(BooleanToBrushConverter),
        new PropertyMetadata(default(Brush)));

    public Brush? TrueBrush
    {
        get => (Brush?)GetValue(TrueBrushProperty);
        set => SetValue(TrueBrushProperty, value);
    }

    public Brush? FalseBrush
    {
        get => (Brush?)GetValue(FalseBrushProperty);
        set => SetValue(FalseBrushProperty, value);
    }

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? TrueBrush : FalseBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
