// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Xml;
using System.Text;
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
    /// Reads the direct profile name from a WLAN profile.
    /// </summary>
    /// <param name="profilePath">Path to the WLAN profile XML file.</param>
    /// <returns>The exact profile name, or <see langword="null"/> when it cannot be read.</returns>
    public static string? TryReadName(string? profilePath)
    {
        return TryReadValue(profilePath, true, "name");
    }

    /// <summary>
    /// Reads the authentication value at the WLAN security path.
    /// </summary>
    /// <param name="profilePath">Path to the WLAN profile XML file.</param>
    /// <returns>The trimmed authentication value, or <see langword="null"/> when it cannot be read.</returns>
    public static string? TryReadAuthentication(string? profilePath)
    {
        return TryReadValue(profilePath, false, "MSM", "security", "authEncryption", "authentication");
    }

    /// <summary>Reads the exact SSID byte identity, independently of the WLAN profile display name.</summary>
    public static string? TryReadSsidHex(string? profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath) || !File.Exists(profilePath)) return null;
        try
        {
            XElement? ssid = ReadPath(LoadProfile(profilePath), "SSIDConfig", "SSID");
            if (ssid is null || ssid.Elements(WlanProfileNamespace + "hex").Skip(1).Any() ||
                ssid.Elements(WlanProfileNamespace + "name").Skip(1).Any()) return null;
            XElement? hex = ReadPath(ssid, "hex");
            if (hex is not null)
            {
                if (hex.HasElements) return null;
                string value = hex.Value;
                if (value.Length is < 2 or > 64 || value.Length % 2 != 0 || !value.All(Uri.IsHexDigit)) return null;
                return Convert.ToHexString(Convert.FromHexString(value));
            }
            XElement? nameElement = ReadPath(ssid, "name");
            if (nameElement?.HasElements == true) return null;
            string? name = nameElement?.Value;
            if (string.IsNullOrEmpty(name)) return null;
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(name);
            return bytes.Length is >= 1 and <= 32 ? Convert.ToHexString(bytes) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or XmlException or ArgumentException)
        {
            return null;
        }
    }

    private static string? TryReadValue(string? profilePath, bool preserveValue, params string[] path)
    {
        if (string.IsNullOrWhiteSpace(profilePath) || !File.Exists(profilePath))
        {
            return null;
        }

        try
        {
            XElement? element = ReadPath(LoadProfile(profilePath), path);
            if (element is null || element.HasElements) return null;
            string value = preserveValue ? element.Value : element.Value.Trim();
            return value.Length == 0 ? null : value;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or XmlException or ArgumentException)
        {
            return null;
        }
    }

    private static XElement? LoadProfile(string profilePath)
    {
        using XmlReader reader = XmlReader.Create(profilePath, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 1024 * 1024
        });
        XElement? root = XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root;
        return root?.Name == WlanProfileNamespace + "WLANProfile" ? root : null;
    }

    private static XElement? ReadPath(XElement? element, params string[] path)
    {
        foreach (string name in path)
        {
            XElement[] children = element?.Elements(WlanProfileNamespace + name).Take(2).ToArray() ?? [];
            if (children.Length != 1) return null;
            element = children[0];
        }
        return element;
    }
}
