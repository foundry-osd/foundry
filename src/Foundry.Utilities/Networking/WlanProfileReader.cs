// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
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
    /// Reads the first non-empty authentication value from a WLAN profile.
    /// </summary>
    /// <param name="profilePath">Path to the WLAN profile XML file.</param>
    /// <returns>The trimmed authentication value, or <see langword="null"/> when it cannot be read.</returns>
    public static string? TryReadAuthentication(string? profilePath)
    {
        return TryReadValue(profilePath, "authentication");
    }

    /// <summary>
    /// Detects whether WLAN profile XML embeds key material, that is any element whose local name is <c>keyMaterial</c>,
    /// whatever its namespace.
    /// </summary>
    /// <remarks>
    /// The XML is read from the bytes, so the byte order mark and the encoding declaration decide the decoding, and no string of the
    /// whole document is created. DTD processing is prohibited and no external resource is resolved. A document that cannot be read
    /// to its end returns <see cref="WlanProfileKeyMaterial.Unreadable"/>, never <see cref="WlanProfileKeyMaterial.Absent"/>.
    /// </remarks>
    /// <param name="profileXml">The WLAN profile XML bytes; the stream is read but not disposed.</param>
    /// <returns>The detection result.</returns>
    public static WlanProfileKeyMaterial DetectKeyMaterial(Stream profileXml)
    {
        ArgumentNullException.ThrowIfNull(profileXml);
        XmlReaderSettings settings = CreateReaderSettings();
        settings.CloseInput = false;
        try
        {
            using XmlReader reader = XmlReader.Create(profileXml, settings);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element &&
                    string.Equals(reader.LocalName, "keyMaterial", StringComparison.Ordinal))
                {
                    return WlanProfileKeyMaterial.Present;
                }
            }

            return WlanProfileKeyMaterial.Absent;
        }
        catch (Exception exception) when (exception is XmlException or DecoderFallbackException)
        {
            return WlanProfileKeyMaterial.Unreadable;
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
