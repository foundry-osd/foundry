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
        string edition = OperatingSystemDisplayFormatter.FormatEdition(item.Edition);
        string channel = OperatingSystemDisplayFormatter.FormatLicenseChannel(item.LicenseChannel);
        string windowsRelease = OperatingSystemDisplayFormatter.FormatWindowsRelease(item.WindowsRelease);

        return $"{windowsRelease} {item.ReleaseId} | {item.Architecture} | {language} | {edition} | {channel} | {item.Build}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
