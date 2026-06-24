// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;

namespace Foundry.Core.Services.Configuration;

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
