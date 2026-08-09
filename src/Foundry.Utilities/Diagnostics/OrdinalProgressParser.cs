// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Text.RegularExpressions;

namespace Foundry.Utilities.Diagnostics;

/// <summary>
/// Parses English and French ordinal progress embedded in diagnostic output.
/// </summary>
public static class OrdinalProgressParser
{
    private static readonly Regex OrdinalRegex = new(
        @"(?<![\d-])(?<current>-?\d+)\s+(?:of|sur)\s+(?<total>-?\d+)(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Tries to convert a positive current/total pair into a clamped percentage.
    /// </summary>
    public static bool TryParse(string? line, out double percent)
    {
        percent = 0d;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        Match match = OrdinalRegex.Match(line);
        if (!match.Success ||
            !int.TryParse(match.Groups["current"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int current) ||
            !int.TryParse(match.Groups["total"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int total) ||
            current <= 0 ||
            total <= 0)
        {
            return false;
        }

        percent = Math.Clamp((double)current / total * 100d, 0d, 100d);
        return true;
    }
}
