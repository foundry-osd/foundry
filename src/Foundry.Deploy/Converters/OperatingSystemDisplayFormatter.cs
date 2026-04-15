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
            ? $"{Foundry.Deploy.Services.Localization.LocalizationText.GetString("OperatingSystem.Windows")} {normalized}"
            : normalized;
    }

    public static string FormatLicenseChannel(string channel)
    {
        return channel.Trim().ToUpperInvariant() switch
        {
            "RET" => Foundry.Deploy.Services.Localization.LocalizationText.GetString("OperatingSystem.Retail"),
            "VOL" => Foundry.Deploy.Services.Localization.LocalizationText.GetString("OperatingSystem.Volume"),
            _ => channel
        };
    }

    public static string FormatEdition(string edition)
    {
        return edition.Trim().ToUpperInvariant() switch
        {
            "PRO" => Foundry.Deploy.Services.Localization.LocalizationText.GetString("OperatingSystem.Professional"),
            "PRO N" => Foundry.Deploy.Services.Localization.LocalizationText.GetString("OperatingSystem.ProfessionalN"),
            _ => edition
        };
    }
}
