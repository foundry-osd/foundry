namespace Foundry.Deploy.Converters;

internal static class OperatingSystemDisplayFormatter
{
    public static string FormatWindowsRelease(string value)
    {
        string normalized = value.Trim();
        if (normalized.StartsWith("Windows ", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return int.TryParse(normalized, out _)
            ? $"Windows {normalized}"
            : normalized;
    }

    public static string FormatLicenseChannel(string channel)
    {
        return channel.Trim().ToUpperInvariant() switch
        {
            "RET" => "Retail",
            "VOL" => "Volume",
            _ => channel
        };
    }

    public static string FormatEdition(string edition)
    {
        return edition.Trim().ToUpperInvariant() switch
        {
            "PRO" => "Professional",
            "PRO N" => "Professional N",
            _ => edition
        };
    }
}
