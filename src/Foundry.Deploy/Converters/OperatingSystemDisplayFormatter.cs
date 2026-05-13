using Foundry.Deploy.Services.Localization;

namespace Foundry.Deploy.Converters;

/// <summary>
/// Formats operating system catalog values for display without changing the invariant filter values.
/// </summary>
internal static class OperatingSystemDisplayFormatter
{
    /// <summary>
    /// Normalizes a Windows release label from catalog metadata.
    /// </summary>
    /// <param name="value">The catalog release value.</param>
    /// <returns>The trimmed release label.</returns>
    public static string FormatWindowsRelease(string value)
    {
        return value.Trim();
    }

    /// <summary>
    /// Converts known license channel codes to culture-specific display labels.
    /// </summary>
    /// <param name="channel">The catalog license channel code or label.</param>
    /// <returns>The localized display label for known channel codes, otherwise the trimmed source value.</returns>
    public static string FormatLicenseChannel(string channel)
    {
        return channel.Trim().ToUpperInvariant() switch
        {
            "RET" => LocalizationText.GetString("Catalog.LicenseChannelRetail"),
            "VOL" => LocalizationText.GetString("Catalog.LicenseChannelVolume"),
            _ => channel.Trim()
        };
    }

    /// <summary>
    /// Converts known Windows edition names to culture-specific display labels.
    /// </summary>
    /// <param name="edition">The catalog edition name.</param>
    /// <returns>The localized display label for known editions, otherwise the trimmed source value.</returns>
    public static string FormatEdition(string edition)
    {
        return edition.Trim() switch
        {
            "Home" => LocalizationText.GetString("Catalog.EditionHome"),
            "Home N" => LocalizationText.GetString("Catalog.EditionHomeN"),
            "Home Single Language" => LocalizationText.GetString("Catalog.EditionHomeSingleLanguage"),
            "Education" => LocalizationText.GetString("Catalog.EditionEducation"),
            "Education N" => LocalizationText.GetString("Catalog.EditionEducationN"),
            "Pro" => LocalizationText.GetString("Catalog.EditionPro"),
            "Pro N" => LocalizationText.GetString("Catalog.EditionProN"),
            "Enterprise" => LocalizationText.GetString("Catalog.EditionEnterprise"),
            "Enterprise N" => LocalizationText.GetString("Catalog.EditionEnterpriseN"),
            _ => edition.Trim()
        };
    }
}
