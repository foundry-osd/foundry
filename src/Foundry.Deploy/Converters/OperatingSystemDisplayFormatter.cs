namespace Foundry.Deploy.Converters;

internal static class OperatingSystemDisplayFormatter
{
    public static string FormatWindowsRelease(string value)
    {
        return value.Trim();
    }

    public static string FormatLicenseChannel(string channel)
    {
        return channel.Trim().ToUpperInvariant() switch
        {
            "RET" => "Retail",
            "VOL" => "Volume",
            _ => channel.Trim()
        };
    }

    public static string FormatEdition(string edition)
    {
        return edition.Trim();
    }
}
