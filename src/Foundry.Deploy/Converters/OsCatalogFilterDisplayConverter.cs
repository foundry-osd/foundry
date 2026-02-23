using System.Globalization;
using System.Windows.Data;

namespace Foundry.Deploy.Converters;

public sealed class OsCatalogFilterDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string source || string.IsNullOrWhiteSpace(source))
        {
            return value;
        }

        string mode = parameter as string ?? string.Empty;
        return mode.ToUpperInvariant() switch
        {
            "LICENSECHANNEL" => OperatingSystemDisplayFormatter.FormatLicenseChannel(source),
            "EDITION" => OperatingSystemDisplayFormatter.FormatEdition(source),
            "WINDOWSRELEASE" => OperatingSystemDisplayFormatter.FormatWindowsRelease(source),
            _ => source
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
