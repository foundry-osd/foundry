using System.Globalization;

namespace Foundry.Services.Configuration;

internal static class LanguageCodeUtility
{
    public static string Canonicalize(string? languageCode)
    {
        string normalized = NormalizeInput(languageCode);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        try
        {
            return CultureInfo.GetCultureInfo(normalized).Name;
        }
        catch (CultureNotFoundException)
        {
            return normalized;
        }
    }

    public static string NormalizeForComparison(string? languageCode)
    {
        return Canonicalize(languageCode).ToLowerInvariant();
    }

    private static string NormalizeInput(string? languageCode)
    {
        return string.IsNullOrWhiteSpace(languageCode)
            ? string.Empty
            : languageCode.Trim().Replace('_', '-');
    }
}
