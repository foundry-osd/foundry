// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network.Native;
using Foundry.Core.Services.Configuration;

namespace Foundry.Connect.Network.Policy;

/// <summary>
/// Security the profile factory can write for a Wi-Fi network.
/// </summary>
public enum WifiProfileSecurity
{
    /// <summary>No authentication or encryption.</summary>
    Open,

    /// <summary>Opportunistic Wireless Encryption.</summary>
    Owe,

    /// <summary>WPA2-Personal.</summary>
    Wpa2Personal,

    /// <summary>WPA3-Personal (SAE).</summary>
    Wpa3Personal,

    /// <summary>WPA3-Personal with WPA2 transition mode.</summary>
    Wpa2Wpa3Personal
}

/// <summary>
/// Classifies Wi-Fi security from native algorithms instead of display labels.
/// </summary>
public static class WifiSecurityClassifier
{
    private const string LegacyPersonalName = "Personal";

    /// <summary>
    /// Maps a native authentication algorithm to a security type.
    /// </summary>
    /// <param name="algorithm">Authentication algorithm reported by wlanapi.</param>
    /// <param name="securityEnabled">Whether the network requires security.</param>
    /// <returns>The security type; <see cref="WifiSecurityType.Unknown"/> for unrecognized values.</returns>
    public static WifiSecurityType Classify(WlanAuthAlgorithm algorithm, bool securityEnabled)
    {
        if (!securityEnabled)
        {
            return WifiSecurityType.Open;
        }

        return algorithm switch
        {
            WlanAuthAlgorithm.Open80211 or WlanAuthAlgorithm.SharedKey => WifiSecurityType.Wep,
            WlanAuthAlgorithm.WpaPsk => WifiSecurityType.WpaPersonal,
            WlanAuthAlgorithm.RsnaPsk => WifiSecurityType.Wpa2Personal,
            WlanAuthAlgorithm.Wpa3Sae => WifiSecurityType.Wpa3Personal,
            WlanAuthAlgorithm.Owe => WifiSecurityType.Owe,
            WlanAuthAlgorithm.Wpa => WifiSecurityType.WpaEnterprise,
            WlanAuthAlgorithm.Rsna => WifiSecurityType.Wpa2Enterprise,
            WlanAuthAlgorithm.Wpa3Enterprise192 or WlanAuthAlgorithm.Wpa3Enterprise => WifiSecurityType.Wpa3Enterprise,
            _ => WifiSecurityType.Unknown
        };
    }

    /// <summary>
    /// Returns the profile security for a discovered network, or <see langword="null"/> when the network cannot be
    /// connected from the list (WEP, WPA1, enterprise, unknown).
    /// </summary>
    /// <param name="security">Classified security type of a discovered network.</param>
    /// <returns>The profile security, or <see langword="null"/>.</returns>
    public static WifiProfileSecurity? ToProfileSecurity(WifiSecurityType security)
    {
        return security switch
        {
            WifiSecurityType.Open => WifiProfileSecurity.Open,
            WifiSecurityType.Owe => WifiProfileSecurity.Owe,
            WifiSecurityType.Wpa2Personal => WifiProfileSecurity.Wpa2Personal,
            WifiSecurityType.Wpa3Personal => WifiProfileSecurity.Wpa3Personal,
            _ => null
        };
    }

    /// <summary>
    /// Maps the schema-5 <c>wifi.securityType</c> value to a profile security.
    /// </summary>
    /// <param name="securityType">Configured value; a type name, never a secret, so it is trimmed.</param>
    /// <returns>The profile security, or <see langword="null"/> for enterprise, unknown or missing values.</returns>
    public static WifiProfileSecurity? FromConfiguredSecurityType(string? securityType)
    {
        string? value = securityType?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (Matches(value, NetworkConfigurationValidator.WifiSecurityOpen))
        {
            return WifiProfileSecurity.Open;
        }

        if (Matches(value, NetworkConfigurationValidator.WifiSecurityOwe))
        {
            return WifiProfileSecurity.Owe;
        }

        if (Matches(value, NetworkConfigurationValidator.WifiSecurityPersonal))
        {
            return WifiProfileSecurity.Wpa2Wpa3Personal;
        }

        if (Matches(value, NetworkConfigurationValidator.WifiSecurityWpa3Personal))
        {
            return WifiProfileSecurity.Wpa3Personal;
        }

        if (Matches(value, NetworkConfigurationValidator.WifiSecurityLegacyWpa2Personal) || Matches(value, LegacyPersonalName))
        {
            return WifiProfileSecurity.Wpa2Personal;
        }

        return null;
    }

    /// <summary>
    /// Returns the <c>connect_wifi_security_type</c> telemetry value, which stays <c>open|owe|personal|enterprise|unknown</c>.
    /// </summary>
    /// <param name="security">Classified security type.</param>
    /// <returns>The telemetry category.</returns>
    public static string TelemetryCategory(WifiSecurityType security)
    {
        return security switch
        {
            WifiSecurityType.Open => "open",
            WifiSecurityType.Owe => "owe",
            WifiSecurityType.Wep or
            WifiSecurityType.WpaPersonal or
            WifiSecurityType.Wpa2Personal or
            WifiSecurityType.Wpa3Personal => "personal",
            WifiSecurityType.WpaEnterprise or
            WifiSecurityType.Wpa2Enterprise or
            WifiSecurityType.Wpa3Enterprise => "enterprise",
            _ => "unknown"
        };
    }

    private static bool Matches(string value, string expected)
    {
        return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    }
}
