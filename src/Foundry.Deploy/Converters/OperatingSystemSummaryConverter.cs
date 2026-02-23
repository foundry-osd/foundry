using System.Globalization;
using System.Windows.Data;
using Foundry.Deploy.Models;

namespace Foundry.Deploy.Converters;

public sealed class OperatingSystemSummaryConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not OperatingSystemCatalogItem item)
        {
            return Binding.DoNothing;
        }

        string language = string.IsNullOrWhiteSpace(item.LanguageCode) ? item.Language : item.LanguageCode;
        string edition = MapEdition(item.Edition);
        string channel = MapLicenseChannel(item.LicenseChannel);

        return $"Windows {item.WindowsRelease} {item.ReleaseId} | {item.Architecture} | {language} | {edition} | {channel} | {item.Build}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }

    private static string MapLicenseChannel(string channel)
    {
        if (channel.Equals("RET", StringComparison.OrdinalIgnoreCase))
        {
            return "Retail";
        }

        if (channel.Equals("VOL", StringComparison.OrdinalIgnoreCase))
        {
            return "Volume";
        }

        return channel;
    }

    private static string MapEdition(string edition)
    {
        if (edition.Equals("Pro", StringComparison.OrdinalIgnoreCase))
        {
            return "Professional";
        }

        if (edition.Equals("Pro N", StringComparison.OrdinalIgnoreCase))
        {
            return "Professional N";
        }

        return edition;
    }
}
