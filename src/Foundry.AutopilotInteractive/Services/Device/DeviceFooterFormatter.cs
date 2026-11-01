// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.AutopilotInteractive.Services.Device;

/// <summary>
/// Builds the single-line device description shown in the footer of every page.
/// </summary>
public static class DeviceFooterFormatter
{
    private const string Separator = " · ";

    /// <summary>
    /// Joins manufacturer, model and serial number, then the computer name when one is configured.
    /// </summary>
    /// <param name="info">Device identity; blank members are replaced by <paramref name="unknownText"/>.</param>
    /// <param name="computerName">Final computer name, appended only when it is not blank.</param>
    /// <param name="unknownText">Localized text standing in for a missing hardware value.</param>
    /// <returns>For example <c>Dell Inc. &#183; Latitude 7440 &#183; 7XK2ABC &#183; PC-001</c>.</returns>
    public static string Format(DeviceInformation info, string? computerName, string unknownText)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(unknownText);

        List<string> parts =
        [
            ValueOrUnknown(info.Manufacturer, unknownText),
            ValueOrUnknown(info.Model, unknownText),
            ValueOrUnknown(info.SerialNumber, unknownText)
        ];

        if (!string.IsNullOrWhiteSpace(computerName))
        {
            parts.Add(computerName.Trim());
        }

        return string.Join(Separator, parts);
    }

    private static string ValueOrUnknown(string? value, string unknownText)
    {
        return string.IsNullOrWhiteSpace(value) ? unknownText : value.Trim();
    }
}
