using System.Globalization;
using System.Windows.Data;

namespace Foundry.Connect.Converters;

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool booleanValue ? !booleanValue : Binding.DoNothing;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool booleanValue ? !booleanValue : Binding.DoNothing;
    }
}
