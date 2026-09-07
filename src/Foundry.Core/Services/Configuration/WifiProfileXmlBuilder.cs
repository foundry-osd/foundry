// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security;
using System.Text;
using System.Xml;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Builds WLAN profile XML for Foundry-managed open, OWE, and personal Wi-Fi profiles.
/// </summary>
public static class WifiProfileXmlBuilder
{
    private const string WifiSecurityLegacyWpa2Personal = "WPA2-Personal";
    private const string WifiSecurityWpa3Personal = "WPA3-Personal";
    private const string WifiSecurityLegacyPersonal = "Personal";

    /// <summary>
    /// Builds WLAN profile XML.
    /// </summary>
    /// <param name="ssidValue">The SSID value.</param>
    /// <param name="securityType">The Foundry Wi-Fi security type.</param>
    /// <param name="passphraseValue">The optional personal Wi-Fi passphrase.</param>
    /// <param name="ssidHexOverride">The optional SSID hex value discovered by Windows.</param>
    /// <returns>The WLAN profile XML.</returns>
    public static string Build(
        string ssidValue,
        string securityType,
        string? passphraseValue,
        string? ssidHexOverride = null)
    {
        string ssidHex = GetSsidHex(ssidValue, ssidHexOverride);
        string profileName = GetProfileName(ssidValue, ssidHexOverride);
        string ssid = EscapeXmlText(profileName);
        string ssidName = profileName == ssidValue ? $"<name>{ssid}</name>" : string.Empty;

        if (string.Equals(securityType, NetworkConfigurationValidator.WifiSecurityOpen, StringComparison.OrdinalIgnoreCase))
        {
            return $$"""
<?xml version="1.0"?>
<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
  <name>{{ssid}}</name>
  <SSIDConfig>
    <SSID>
      <hex>{{ssidHex}}</hex>
      {{ssidName}}
    </SSID>
  </SSIDConfig>
  <connectionType>ESS</connectionType>
  <connectionMode>manual</connectionMode>
  <MSM>
    <security>
      <authEncryption>
        <authentication>open</authentication>
        <encryption>none</encryption>
        <useOneX>false</useOneX>
      </authEncryption>
    </security>
  </MSM>
  <MacRandomization xmlns="http://www.microsoft.com/networking/WLAN/profile/v3">
    <enableRandomization>false</enableRandomization>
  </MacRandomization>
</WLANProfile>
""";
        }

        if (IsPersonalSecurityType(securityType))
        {
            string passphrase = EscapeXmlText(passphraseValue ?? string.Empty);
            string authentication = ResolvePersonalAuthentication(securityType);
            string transitionMode = string.Equals(securityType, NetworkConfigurationValidator.WifiSecurityPersonal, StringComparison.OrdinalIgnoreCase)
                ? """
        <transitionMode xmlns="http://www.microsoft.com/networking/WLAN/profile/v4">true</transitionMode>
"""
                : string.Empty;

            return $$"""
<?xml version="1.0"?>
<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
  <name>{{ssid}}</name>
  <SSIDConfig>
    <SSID>
      <hex>{{ssidHex}}</hex>
      {{ssidName}}
    </SSID>
  </SSIDConfig>
  <connectionType>ESS</connectionType>
  <connectionMode>manual</connectionMode>
  <MSM>
    <security>
      <authEncryption>
        <authentication>{{authentication}}</authentication>
        <encryption>AES</encryption>
        <useOneX>false</useOneX>
{{transitionMode}}      </authEncryption>
      <sharedKey>
        <keyType>passPhrase</keyType>
        <protected>false</protected>
        <keyMaterial>{{passphrase}}</keyMaterial>
      </sharedKey>
    </security>
  </MSM>
  <MacRandomization xmlns="http://www.microsoft.com/networking/WLAN/profile/v3">
    <enableRandomization>false</enableRandomization>
  </MacRandomization>
</WLANProfile>
""";
        }

        if (string.Equals(securityType, NetworkConfigurationValidator.WifiSecurityOwe, StringComparison.OrdinalIgnoreCase))
        {
            return $$"""
<?xml version="1.0"?>
<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
  <name>{{ssid}}</name>
  <SSIDConfig>
    <SSID>
      <hex>{{ssidHex}}</hex>
      {{ssidName}}
    </SSID>
  </SSIDConfig>
  <connectionType>ESS</connectionType>
  <connectionMode>manual</connectionMode>
  <MSM>
    <security>
      <authEncryption>
        <authentication>OWE</authentication>
        <encryption>AES</encryption>
        <useOneX>false</useOneX>
      </authEncryption>
    </security>
  </MSM>
  <MacRandomization xmlns="http://www.microsoft.com/networking/WLAN/profile/v3">
    <enableRandomization>false</enableRandomization>
  </MacRandomization>
</WLANProfile>
""";
        }

        throw new InvalidOperationException($"Unsupported Wi-Fi security type '{securityType}'.");
    }

    public static string GetSsidHex(string? ssidValue, string? ssidHexOverride = null)
    {
        if (ssidHexOverride is not null)
        {
            if (ssidHexOverride.Length is < 2 or > 64 || ssidHexOverride.Length % 2 != 0 ||
                !ssidHexOverride.All(Uri.IsHexDigit))
                throw new ArgumentException("SSID hex must encode between 1 and 32 bytes.", nameof(ssidHexOverride));
            return Convert.ToHexString(Convert.FromHexString(ssidHexOverride));
        }
        if (string.IsNullOrEmpty(ssidValue)) throw new ArgumentException("An SSID is required.", nameof(ssidValue));
        byte[] bytes = new UTF8Encoding(false, true).GetBytes(ssidValue);
        if (bytes.Length is < 1 or > 32) throw new ArgumentException("SSID must contain between 1 and 32 UTF-8 bytes.", nameof(ssidValue));
        return Convert.ToHexString(bytes);
    }

    public static string GetProfileName(string ssidValue, string? ssidHexOverride = null)
    {
        string identity = GetSsidHex(ssidValue, ssidHexOverride);
        try
        {
            if (GetSsidHex(ssidValue) == identity)
            {
                XmlConvert.VerifyXmlChars(ssidValue);
                return ssidValue;
            }
        }
        catch (Exception error) when (error is ArgumentException or XmlException) { }
        return "Foundry-SSID-" + identity;
    }

    private static string EscapeXmlText(string value) =>
        (SecurityElement.Escape(value) ?? string.Empty).Replace("\r", "&#xD;", StringComparison.Ordinal);

    private static bool IsPersonalSecurityType(string securityType)
    {
        return string.Equals(securityType, WifiSecurityLegacyWpa2Personal, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(securityType, NetworkConfigurationValidator.WifiSecurityPersonal, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(securityType, WifiSecurityWpa3Personal, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(securityType, WifiSecurityLegacyPersonal, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolvePersonalAuthentication(string securityType)
    {
        if (string.Equals(securityType, NetworkConfigurationValidator.WifiSecurityPersonal, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(securityType, WifiSecurityWpa3Personal, StringComparison.OrdinalIgnoreCase))
        {
            return "WPA3SAE";
        }

        return "WPA2PSK";
    }

}
