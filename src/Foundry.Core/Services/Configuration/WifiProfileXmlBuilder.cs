// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security;
using System.Text;

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
        string trimmedSsid = ssidValue.Trim();
        string ssid = SecurityElement.Escape(trimmedSsid) ?? string.Empty;
        string ssidHex = string.IsNullOrWhiteSpace(ssidHexOverride)
            ? ConvertSsidToHex(trimmedSsid)
            : ssidHexOverride.Trim();

        if (string.Equals(securityType, NetworkConfigurationValidator.WifiSecurityOpen, StringComparison.OrdinalIgnoreCase))
        {
            return $$"""
<?xml version="1.0"?>
<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
  <name>{{ssid}}</name>
  <SSIDConfig>
    <SSID>
      <hex>{{ssidHex}}</hex>
      <name>{{ssid}}</name>
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
            string passphrase = SecurityElement.Escape(passphraseValue?.Trim() ?? string.Empty) ?? string.Empty;
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
      <name>{{ssid}}</name>
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
      <name>{{ssid}}</name>
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

    private static string ConvertSsidToHex(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        StringBuilder builder = new(bytes.Length * 2);

        foreach (byte currentByte in bytes)
        {
            builder.Append(currentByte.ToString("X2"));
        }

        return builder.ToString();
    }
}
