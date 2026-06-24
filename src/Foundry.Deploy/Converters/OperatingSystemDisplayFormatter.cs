// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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

}
