using Foundry.Services.Theme;
using Microsoft.UI.Xaml.Data;

namespace Foundry.Converters;

public class ThemeModeToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is ThemeMode currentTheme
            && parameter is string targetThemeName
            && Enum.TryParse<ThemeMode>(targetThemeName, out var targetTheme))
        {
            return currentTheme == targetTheme;
        }

        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
