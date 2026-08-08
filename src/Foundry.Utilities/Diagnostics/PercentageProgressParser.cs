// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Text.RegularExpressions;

namespace Foundry.Utilities.Diagnostics;

/// <summary>
/// Parses percentage values embedded in diagnostic output.
/// </summary>
public static class PercentageProgressParser
{
    private static readonly Regex PercentageRegex = new(
        @"(?<percent>\d{1,3}(?:[.,]\d+)?)\s*%",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Tries to parse and clamp the first percentage in a line.
    /// </summary>
    public static bool TryParse(string? line, out double percent)
    {
        percent = 0d;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        Match match = PercentageRegex.Match(line);
        if (!match.Success)
        {
            return false;
        }

        string rawPercent = match.Groups["percent"].Value.Replace(',', '.');
        if (!double.TryParse(rawPercent, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return false;
        }

        percent = Math.Clamp(parsed, 0d, 100d);
        return true;
    }
}
