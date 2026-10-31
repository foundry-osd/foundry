// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Network.Native;
using Foundry.Connect.Network.Policy;

namespace Foundry.Connect.Tests.Network;

public sealed class WifiSecurityClassifierTests
{
    [Theory]
    [InlineData(WlanAuthAlgorithm.Open80211, false, WifiSecurityType.Open)]
    [InlineData(WlanAuthAlgorithm.RsnaPsk, false, WifiSecurityType.Open)]
    [InlineData(WlanAuthAlgorithm.Open80211, true, WifiSecurityType.Wep)]
    [InlineData(WlanAuthAlgorithm.SharedKey, true, WifiSecurityType.Wep)]
    [InlineData(WlanAuthAlgorithm.WpaPsk, true, WifiSecurityType.WpaPersonal)]
    [InlineData(WlanAuthAlgorithm.RsnaPsk, true, WifiSecurityType.Wpa2Personal)]
    [InlineData(WlanAuthAlgorithm.Wpa3Sae, true, WifiSecurityType.Wpa3Personal)]
    [InlineData(WlanAuthAlgorithm.Owe, true, WifiSecurityType.Owe)]
    [InlineData(WlanAuthAlgorithm.Wpa, true, WifiSecurityType.WpaEnterprise)]
    [InlineData(WlanAuthAlgorithm.Rsna, true, WifiSecurityType.Wpa2Enterprise)]
    [InlineData(WlanAuthAlgorithm.Wpa3Enterprise192, true, WifiSecurityType.Wpa3Enterprise)]
    [InlineData(WlanAuthAlgorithm.Wpa3Enterprise, true, WifiSecurityType.Wpa3Enterprise)]
    [InlineData(WlanAuthAlgorithm.WpaNone, true, WifiSecurityType.Unknown)]
    [InlineData(WlanAuthAlgorithm.IhvStart, true, WifiSecurityType.Unknown)]
    public void Classify_MapsNativeAlgorithms(WlanAuthAlgorithm algorithm, bool securityEnabled, WifiSecurityType expected)
    {
        Assert.Equal(expected, WifiSecurityClassifier.Classify(algorithm, securityEnabled));
    }

    [Theory]
    [InlineData(WifiSecurityType.Open, WifiProfileSecurity.Open)]
    [InlineData(WifiSecurityType.Owe, WifiProfileSecurity.Owe)]
    [InlineData(WifiSecurityType.Wpa2Personal, WifiProfileSecurity.Wpa2Personal)]
    [InlineData(WifiSecurityType.Wpa3Personal, WifiProfileSecurity.Wpa3Personal)]
    [InlineData(WifiSecurityType.Wep, null)]
    [InlineData(WifiSecurityType.WpaPersonal, null)]
    [InlineData(WifiSecurityType.Wpa2Enterprise, null)]
    [InlineData(WifiSecurityType.Unknown, null)]
    public void ToProfileSecurity_AllowsOnlySupportedDiscoveredTypes(WifiSecurityType security, WifiProfileSecurity? expected)
    {
        Assert.Equal(expected, WifiSecurityClassifier.ToProfileSecurity(security));
    }

    [Theory]
    [InlineData("Open", WifiProfileSecurity.Open)]
    [InlineData("OWE", WifiProfileSecurity.Owe)]
    [InlineData("WPA2/WPA3-Personal", WifiProfileSecurity.Wpa2Wpa3Personal)]
    [InlineData("WPA3-Personal", WifiProfileSecurity.Wpa3Personal)]
    [InlineData("WPA2-Personal", WifiProfileSecurity.Wpa2Personal)]
    [InlineData("Personal", WifiProfileSecurity.Wpa2Personal)]
    [InlineData("wpa2-personal", WifiProfileSecurity.Wpa2Personal)]
    [InlineData("WPA2/WPA3-Enterprise", null)]
    [InlineData(null, null)]
    public void FromConfiguredSecurityType_KeepsTodaysMapping(string? configured, WifiProfileSecurity? expected)
    {
        Assert.Equal(expected, WifiSecurityClassifier.FromConfiguredSecurityType(configured));
    }

    [Theory]
    [InlineData(WifiSecurityType.Open, "open")]
    [InlineData(WifiSecurityType.Owe, "owe")]
    [InlineData(WifiSecurityType.Wep, "personal")]
    [InlineData(WifiSecurityType.Wpa3Personal, "personal")]
    [InlineData(WifiSecurityType.Wpa2Enterprise, "enterprise")]
    [InlineData(WifiSecurityType.Unknown, "unknown")]
    public void TelemetryCategory_KeepsTheExistingValueSet(WifiSecurityType security, string expected)
    {
        Assert.Equal(expected, WifiSecurityClassifier.TelemetryCategory(security));
    }
}
