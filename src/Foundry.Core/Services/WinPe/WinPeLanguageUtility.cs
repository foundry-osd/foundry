// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;

namespace Foundry.Core.Services.WinPe;

internal static class WinPeLanguageUtility
{
    public static string Normalize(string languageCode)
    {
        return string.IsNullOrWhiteSpace(languageCode)
            ? string.Empty
            : languageCode.Trim().Replace('_', '-').ToLowerInvariant();
    }

    public static string Canonicalize(string languageCode)
    {
        string normalized = Normalize(languageCode);
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

    /// <summary>Lets DISM map a specific Windows locale to its default input profile; custom/neutral names cannot prove a Windows layout.</summary>
    public static bool TryResolveInputLocale(string languageCode, out string canonicalLanguageCode, out string inputLocale)
    {
        canonicalLanguageCode = languageCode;
        inputLocale = string.Empty;
        string normalized = Normalize(languageCode);
        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(normalized);
            if (culture.IsNeutralCulture || culture.LCID is 0x1000 or 0x007f)
            {
                return false;
            }
            canonicalLanguageCode = culture.Name;
            inputLocale = culture.Name;
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
