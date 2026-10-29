// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Foundry.Connect.Network.Policy;

/// <summary>
/// A WLAN profile ready for <c>WlanSetProfile</c>, with the identity needed to connect it.
/// </summary>
/// <remarks>
/// The XML may contain key material. This is a class, not a record, so no generated member prints it, and
/// <see cref="ToString"/> never shows it (T15). <see cref="Clear"/> drops the reference at exit (L2-6); the string
/// itself cannot be zeroed.
/// </remarks>
public sealed class WifiProfileDocument
{
    private const string ProfileNamespace = "http://www.microsoft.com/networking/WLAN/profile/v1";

    private string? _xml;

    /// <summary>
    /// Creates a profile document.
    /// </summary>
    /// <param name="name">Profile name used by <c>WlanConnect</c>.</param>
    /// <param name="ssid">SSID the profile targets, or <see langword="null"/> when unknown.</param>
    /// <param name="isHidden">Whether the profile is marked non-broadcast.</param>
    /// <param name="xml">Profile XML.</param>
    public WifiProfileDocument(string name, WifiSsid? ssid, bool isHidden, string xml)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(xml);

        Name = name;
        Ssid = ssid;
        IsHidden = isHidden;
        _xml = xml;
    }

    /// <summary>
    /// Gets the profile name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the SSID the profile targets, or <see langword="null"/> when the profile does not state one.
    /// </summary>
    public WifiSsid? Ssid { get; }

    /// <summary>
    /// Gets a value indicating whether the profile is marked non-broadcast.
    /// </summary>
    public bool IsHidden { get; }

    /// <summary>
    /// Gets the profile XML.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The document was cleared.</exception>
    public string Xml => _xml ?? throw new ObjectDisposedException(nameof(WifiProfileDocument));

    /// <summary>
    /// Drops the XML reference. Name, SSID and visibility stay readable.
    /// </summary>
    public void Clear()
    {
        _xml = null;
    }

    /// <summary>
    /// Returns a description that never contains the profile XML or the profile name.
    /// </summary>
    /// <returns>The name length only.</returns>
    public override string ToString()
    {
        return $"WifiProfileDocument({Name.Length} chars name)";
    }

    /// <summary>
    /// Reads the identity of a profile XML such as a provisioned template.
    /// </summary>
    /// <param name="xml">WLAN profile XML.</param>
    /// <returns>
    /// The document with the name, the SSID (<c>SSIDConfig/SSID/hex</c>, else <c>name</c>) and the
    /// <c>nonBroadcast</c> flag; <see langword="null"/> when the XML is not a WLAN profile with a name, or when its
    /// SSID value is present but unreadable (invalid hexadecimal, or not 1 to 32 bytes).
    /// </returns>
    public static WifiProfileDocument? FromXml(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        XDocument document;
        try
        {
            XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using StringReader text = new(xml);
            using XmlReader reader = XmlReader.Create(text, settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }

        XNamespace ns = ProfileNamespace;
        XElement? root = document.Root;
        if (root is null || root.Name != ns + "WLANProfile")
        {
            return null;
        }

        string? name = root.Element(ns + "name")?.Value;
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        XElement? ssidConfig = root.Element(ns + "SSIDConfig");
        bool isHidden = string.Equals(ssidConfig?.Element(ns + "nonBroadcast")?.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        if (!TryReadSsid(ssidConfig?.Element(ns + "SSID"), ns, out WifiSsid? ssid))
        {
            return null;
        }

        return new WifiProfileDocument(name, ssid, isHidden, xml);
    }

    /// <summary>
    /// Reads the SSID of a profile. A <c>hex</c> value wins over <c>name</c>; a value that is present but unreadable
    /// fails closed and never falls back to the other element, so a template cannot target an unintended network.
    /// </summary>
    /// <returns><see langword="false"/> when an SSID value is present but unreadable.</returns>
    private static bool TryReadSsid(XElement? ssidElement, XNamespace ns, out WifiSsid? ssid)
    {
        ssid = null;
        if (ssidElement is null)
        {
            return true;
        }

        string? hex = ssidElement.Element(ns + "hex")?.Value.Trim();
        if (!string.IsNullOrEmpty(hex))
        {
            try
            {
                ssid = WifiSsid.FromHex(hex);
                return true;
            }
            catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        string? text = ssidElement.Element(ns + "name")?.Value;
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        return WifiSsid.TryFromBytes(Encoding.UTF8.GetBytes(text), out ssid);
    }
}
