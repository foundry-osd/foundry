using System.Globalization;
using System.Windows.Data;
using Foundry.Services.Theme;

namespace Foundry.Converters;

public class ThemeModeToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ThemeMode currentTheme
            && parameter is string targetThemeName
            && Enum.TryParse<ThemeMode>(targetThemeName, out var targetTheme))
        {
            return currentTheme == targetTheme;
        }

        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
