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
    /// Converts known license channel codes to English display labels.
    /// </summary>
    /// <param name="channel">The catalog license channel code or label.</param>
    /// <returns>The English display label for known channel codes, otherwise the trimmed source value.</returns>
    public static string FormatLicenseChannel(string channel)
    {
        return channel.Trim().ToUpperInvariant() switch
        {
            "RET" => "Retail",
            "VOL" => "Volume",
            _ => channel.Trim()
        };
    }

    /// <summary>
    /// Converts known Windows edition names to English display labels.
    /// </summary>
    /// <param name="edition">The catalog edition name.</param>
    /// <returns>The English display label for known editions, otherwise the trimmed source value.</returns>
    public static string FormatEdition(string edition)
    {
        return edition.Trim() switch
        {
            "Home" => "Home",
            "Home N" => "Home N",
            "Home Single Language" => "Home Single Language",
            "Education" => "Education",
            "Education N" => "Education N",
            "Pro" => "Pro",
            "Pro N" => "Pro N",
            "Enterprise" => "Enterprise",
            "Enterprise N" => "Enterprise N",
            _ => edition.Trim()
        };
    }
}
