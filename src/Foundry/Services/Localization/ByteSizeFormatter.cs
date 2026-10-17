// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;

namespace Foundry.Services.Localization;

/// <summary>
/// Formats byte counts for display with localized units, using 1024-based units like Windows Explorer.
/// </summary>
internal static class ByteSizeFormatter
{
    private static readonly string[] UnitResourceKeys =
    [
        "StartMedia.ByteUnit.B",
        "StartMedia.ByteUnit.KB",
        "StartMedia.ByteUnit.MB",
        "StartMedia.ByteUnit.GB",
        "StartMedia.ByteUnit.TB"
    ];

    /// <summary>
    /// Formats <paramref name="bytes"/> with at most one decimal in the current UI culture, for example "114.6 GB".
    /// </summary>
    public static string Format(IApplicationLocalizationService localizationService, ulong bytes)
    {
        double size = bytes;
        int unitIndex = 0;
        while (size >= 1024 && unitIndex < UnitResourceKeys.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return string.Format(
            CultureInfo.CurrentUICulture,
            "{0:0.#} {1}",
            size,
            localizationService.GetString(UnitResourceKeys[unitIndex]));
    }
}
