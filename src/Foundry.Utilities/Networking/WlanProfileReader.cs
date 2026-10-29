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

    /// <summary>
    /// Gets whether WLAN profile XML embeds key material, that is any element whose local name is <c>keyMaterial</c>,
    /// whatever its namespace.
    /// </summary>
    /// <remarks>DTD processing is prohibited and no external resource is resolved; XML that cannot be parsed returns <see langword="false"/>.</remarks>
    /// <param name="profileXml">The WLAN profile XML text.</param>
    /// <returns><see langword="true"/> when a <c>keyMaterial</c> element is present.</returns>
    public static bool ContainsKeyMaterial(string profileXml)
    {
        ArgumentNullException.ThrowIfNull(profileXml);
        try
        {
            using var textReader = new StringReader(profileXml);
            using XmlReader reader = XmlReader.Create(textReader, CreateReaderSettings());
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element &&
                    string.Equals(reader.LocalName, "keyMaterial", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static XmlReaderSettings CreateReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null
    };

    private static string? TryReadValue(string? profilePath, string elementName)
    {
        if (string.IsNullOrWhiteSpace(profilePath) || !File.Exists(profilePath))
        {
            return null;
        }

        try
        {
            using XmlReader reader = XmlReader.Create(profilePath, CreateReaderSettings());
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
