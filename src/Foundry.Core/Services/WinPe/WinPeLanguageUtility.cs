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

        TryCanonicalize(normalized, out string canonicalLanguageCode);
        return canonicalLanguageCode;
    }

    public static bool TryCanonicalize(string languageCode, out string canonicalLanguageCode)
    {
        try
        {
            canonicalLanguageCode = CultureInfo.GetCultureInfo(languageCode).Name;
            return true;
        }
        catch (CultureNotFoundException)
        {
            canonicalLanguageCode = languageCode;
            return false;
        }
    }
}
