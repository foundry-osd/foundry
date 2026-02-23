using System.Globalization;
using System.Windows.Data;

namespace Foundry.Deploy.Converters;

public sealed class OsCatalogFilterDisplayConverter : IValueConverter
{
    private static readonly Dictionary<string, string> LicenseChannelDisplayMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RET"] = "Retail",
        ["VOL"] = "Volume"
    };

    private static readonly Dictionary<string, string> EditionDisplayMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Home"] = "Home",
        ["Home N"] = "Home N",
        ["Home Single Language"] = "Home Single Language",
        ["Education"] = "Education",
        ["Education N"] = "Education N",
        ["Pro"] = "Professional",
        ["Pro N"] = "Professional N",
        ["Enterprise"] = "Enterprise",
        ["Enterprise N"] = "Enterprise N"
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string source || string.IsNullOrWhiteSpace(source))
        {
            return value;
        }

        string mode = parameter as string ?? string.Empty;
        return mode.Equals("LicenseChannel", StringComparison.OrdinalIgnoreCase)
            ? MapDisplayValue(source, LicenseChannelDisplayMap)
            : mode.Equals("Edition", StringComparison.OrdinalIgnoreCase)
                ? MapDisplayValue(source, EditionDisplayMap)
                : mode.Equals("WindowsRelease", StringComparison.OrdinalIgnoreCase)
                    ? MapWindowsRelease(source)
                : source;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }

    private static string MapDisplayValue(string source, IReadOnlyDictionary<string, string> map)
    {
        return map.TryGetValue(source, out string? display)
            ? display
            : source;
    }

    private static string MapWindowsRelease(string source)
    {
        string value = source.Trim();
        if (value.StartsWith("Windows ", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return int.TryParse(value, out _)
            ? $"Windows {value}"
            : value;
    }
}
