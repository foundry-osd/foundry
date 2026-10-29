// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text;
using System.Xml;

namespace Foundry.Connect.Network.Policy;

/// <summary>
/// Reason a personal Wi-Fi profile cannot be built from the supplied passphrase.
/// </summary>
public enum WifiProfileError
{
    /// <summary>No error.</summary>
    None,

    /// <summary>A personal network needs a passphrase and none was supplied.</summary>
    PassphraseRequired,

    /// <summary>The passphrase has fewer than 8 characters.</summary>
    PassphraseTooShort,

    /// <summary>The passphrase has more than 63 characters (or 64 that are not a WPA2 hexadecimal key).</summary>
    PassphraseTooLong,

    /// <summary>The passphrase contains a character outside printable ASCII.</summary>
    PassphraseInvalidCharacters
}

/// <summary>
/// Result of <see cref="WifiProfileFactory.Build"/>.
/// </summary>
/// <param name="Profile">The profile, or <see langword="null"/> when the input was rejected.</param>
/// <param name="Error">The rejection reason, or <see cref="WifiProfileError.None"/>.</param>
public sealed record WifiProfileBuildResult(WifiProfileDocument? Profile, WifiProfileError Error)
{
    /// <summary>
    /// Gets a value indicating whether a profile was built.
    /// </summary>
    public bool IsSuccess => Profile is not null;
}

/// <summary>
/// Builds WLAN profiles for manual connections and provisioned open or personal networks.
/// </summary>
/// <remarks>
/// The profile XML is written with an <see cref="XmlWriter"/>, never by concatenating the SSID or the passphrase.
/// The passphrase is never trimmed and never logged. The SSID is written as its raw bytes in hexadecimal.
/// </remarks>
public static class WifiProfileFactory
{
    private const string ProfileNamespace = "http://www.microsoft.com/networking/WLAN/profile/v1";
    private const string MacRandomizationNamespace = "http://www.microsoft.com/networking/WLAN/profile/v3";
    private const string TransitionModeNamespace = "http://www.microsoft.com/networking/WLAN/profile/v4";
    private const string XmlDeclaration = "<?xml version=\"1.0\"?>";
    private const int MinimumPassphraseLength = 8;
    private const int MaximumPassphraseLength = 63;
    private const int NetworkKeyLength = 64;

    /// <summary>
    /// Builds a profile for one SSID and security type.
    /// </summary>
    /// <remarks>
    /// Personal types check the passphrase in this order: empty, a character outside <c>0x20</c> to <c>0x7E</c>,
    /// a 64-character hexadecimal key for <see cref="WifiProfileSecurity.Wpa2Personal"/> (written as a
    /// <c>networkKey</c>), shorter than 8, longer than 63. Open and OWE ignore the passphrase.
    /// </remarks>
    /// <param name="ssid">Network SSID.</param>
    /// <param name="security">Security to write.</param>
    /// <param name="passphrase">Passphrase or key; used exactly as given.</param>
    /// <returns>The profile, or the reason the passphrase was rejected.</returns>
    public static WifiProfileBuildResult Build(WifiSsid ssid, WifiProfileSecurity security, ReadOnlySpan<char> passphrase)
    {
        ArgumentNullException.ThrowIfNull(ssid);

        bool isPersonal = security is WifiProfileSecurity.Wpa2Personal or WifiProfileSecurity.Wpa3Personal or WifiProfileSecurity.Wpa2Wpa3Personal;
        bool isNetworkKey = false;
        if (isPersonal)
        {
            WifiProfileError error = ValidatePassphrase(security, passphrase, out isNetworkKey);
            if (error != WifiProfileError.None)
            {
                return new WifiProfileBuildResult(null, error);
            }
        }

        string name = ToXmlText(ssid.DisplayName);
        string xml = WriteProfile(ssid, name, security, isPersonal ? passphrase : ReadOnlySpan<char>.Empty, isNetworkKey);
        return new WifiProfileBuildResult(new WifiProfileDocument(name, ssid, false, xml), WifiProfileError.None);
    }

    private static WifiProfileError ValidatePassphrase(WifiProfileSecurity security, ReadOnlySpan<char> passphrase, out bool isNetworkKey)
    {
        isNetworkKey = false;

        if (passphrase.IsEmpty)
        {
            return WifiProfileError.PassphraseRequired;
        }

        foreach (char character in passphrase)
        {
            if (character is < ' ' or > '~')
            {
                return WifiProfileError.PassphraseInvalidCharacters;
            }
        }

        if (security == WifiProfileSecurity.Wpa2Personal && passphrase.Length == NetworkKeyLength && IsHexadecimal(passphrase))
        {
            isNetworkKey = true;
            return WifiProfileError.None;
        }

        if (passphrase.Length < MinimumPassphraseLength)
        {
            return WifiProfileError.PassphraseTooShort;
        }

        return passphrase.Length > MaximumPassphraseLength ? WifiProfileError.PassphraseTooLong : WifiProfileError.None;
    }

    private static bool IsHexadecimal(ReadOnlySpan<char> value)
    {
        foreach (char character in value)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static string WriteProfile(WifiSsid ssid, string name, WifiProfileSecurity security, ReadOnlySpan<char> passphrase, bool isNetworkKey)
    {
        XmlWriterSettings settings = new()
        {
            OmitXmlDeclaration = true,
            Indent = true,
            IndentChars = "  "
        };

        StringWriter text = new();
        text.WriteLine(XmlDeclaration);
        using (XmlWriter writer = XmlWriter.Create(text, settings))
        {
            writer.WriteStartElement("WLANProfile", ProfileNamespace);
            writer.WriteElementString("name", name);
            writer.WriteStartElement("SSIDConfig");
            writer.WriteStartElement("SSID");
            writer.WriteElementString("hex", ssid.Hex);
            writer.WriteElementString("name", name);
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteElementString("connectionType", "ESS");
            writer.WriteElementString("connectionMode", "manual");
            writer.WriteStartElement("MSM");
            writer.WriteStartElement("security");
            writer.WriteStartElement("authEncryption");
            writer.WriteElementString("authentication", ResolveAuthentication(security));
            writer.WriteElementString("encryption", security == WifiProfileSecurity.Open ? "none" : "AES");
            writer.WriteElementString("useOneX", "false");
            if (security == WifiProfileSecurity.Wpa2Wpa3Personal)
            {
                writer.WriteStartElement(string.Empty, "transitionMode", TransitionModeNamespace);
                writer.WriteString("true");
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            if (!passphrase.IsEmpty)
            {
                writer.WriteStartElement("sharedKey");
                writer.WriteElementString("keyType", isNetworkKey ? "networkKey" : "passPhrase");
                writer.WriteElementString("protected", "false");
                writer.WriteElementString("keyMaterial", new string(passphrase));
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteStartElement(string.Empty, "MacRandomization", MacRandomizationNamespace);
            writer.WriteElementString("enableRandomization", "false");
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        return text.ToString();
    }

    private static string ResolveAuthentication(WifiProfileSecurity security)
    {
        return security switch
        {
            WifiProfileSecurity.Open => "open",
            WifiProfileSecurity.Owe => "OWE",
            WifiProfileSecurity.Wpa2Personal => "WPA2PSK",
            _ => "WPA3SAE"
        };
    }

    /// <summary>
    /// Replaces characters that XML 1.0 cannot carry (control characters in a broadcast SSID) so the writer never throws.
    /// </summary>
    private static string ToXmlText(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char character in value)
        {
            builder.Append(char.IsSurrogate(character) || XmlConvert.IsXmlChar(character) ? character : (char)0xFFFD);
        }

        return builder.ToString();
    }
}
