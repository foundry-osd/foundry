using System.Globalization;
using System.Windows.Data;

namespace Foundry.Converters;

public class CultureToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is CultureInfo currentCulture && parameter is string targetCulture)
        {
            return currentCulture.Name == targetCulture;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
