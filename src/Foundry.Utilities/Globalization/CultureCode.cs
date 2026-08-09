// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;

namespace Foundry.Utilities.Globalization;

/// <summary>
/// Provides normalization for culture codes.
/// </summary>
public static class CultureCode
{
    /// <summary>
    /// Trims and canonicalizes a culture code. Unknown culture codes are returned with whitespace removed and underscores replaced by hyphens.
    /// </summary>
    /// <param name="value">Culture code to canonicalize.</param>
    /// <returns>A canonical culture code, or a normalized unknown culture code.</returns>
    public static string Canonicalize(string? value)
    {
        string normalized = NormalizeInput(value);
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

    /// <summary>
    /// Returns a canonical culture code in invariant lowercase for comparisons.
    /// </summary>
    /// <param name="value">Culture code to normalize.</param>
    /// <returns>A normalized culture code in invariant lowercase.</returns>
    public static string NormalizeForComparison(string? value)
    {
        return Canonicalize(value).ToLowerInvariant();
    }

    private static string NormalizeInput(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace('_', '-');
    }
}
