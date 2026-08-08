// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Xml;
using System.Xml.Linq;

namespace Foundry.Utilities.Networking;

/// <summary>
/// Reads raw values from Windows WLAN profile XML files.
/// </summary>
public static class WlanProfileReader
{
    private static readonly XNamespace WlanProfileNamespace =
        "http://www.microsoft.com/networking/WLAN/profile/v1";

    /// <summary>
    /// Reads the first non-empty profile name from a WLAN profile.
    /// </summary>
    /// <param name="profilePath">Path to the WLAN profile XML file.</param>
    /// <returns>The trimmed profile name, or <see langword="null"/> when it cannot be read.</returns>
    public static string? TryReadName(string? profilePath)
    {
        return TryReadValue(profilePath, "name");
    }

    /// <summary>
    /// Reads the first non-empty authentication value from a WLAN profile.
    /// </summary>
    /// <param name="profilePath">Path to the WLAN profile XML file.</param>
    /// <returns>The trimmed authentication value, or <see langword="null"/> when it cannot be read.</returns>
    public static string? TryReadAuthentication(string? profilePath)
    {
        return TryReadValue(profilePath, "authentication");
    }

    private static string? TryReadValue(string? profilePath, string elementName)
    {
        if (string.IsNullOrWhiteSpace(profilePath) || !File.Exists(profilePath))
        {
            return null;
        }

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            using XmlReader reader = XmlReader.Create(profilePath, settings);
            XDocument document = XDocument.Load(reader);

            return document
                .Descendants(WlanProfileNamespace + elementName)
                .Select(static element => element.Value.Trim())
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        }
        catch
        {
            return null;
        }
    }
}
