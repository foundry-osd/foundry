// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class WifiProfileXmlBuilderTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("GG")]
    [InlineData(" 20")]
    [InlineData("")]
    public void Build_RejectsInvalidAuthoritativeHex(string rawHex)
    {
        Assert.Throws<ArgumentException>(() => WifiProfileXmlBuilder.Build("Lab", "Open", null, rawHex));
    }

    [Fact]
    public void Build_UsesRawHexAndDistinctSafeProfileNameForUnrepresentableIdentity()
    {
        string first = WifiProfileXmlBuilder.Build("�", "Open", null, "ff");
        string second = WifiProfileXmlBuilder.Build("�", "Open", null, "fe");
        Assert.Contains("<hex>FF</hex>", first);
        Assert.Contains("<name>Foundry-SSID-FF</name>", first);
        Assert.Contains("<name>Foundry-SSID-FE</name>", second);
        Assert.DoesNotContain("<name>�</name>", first);
    }

    [Theory]
    [InlineData(" Lab ", " password123 ")]
    [InlineData("   ", "        ")]
    [InlineData("réseau", " éPassword ")]
    [InlineData("Lab\rOne", "pass\rword123")]
    [InlineData("Lab\r\nOne", "pass\r\nword123")]
    public void Build_PreservesExactProtocolValues(string ssid, string password)
    {
        var document = System.Xml.Linq.XDocument.Parse(WifiProfileXmlBuilder.Build(ssid, "WPA2-Personal", password),
            System.Xml.Linq.LoadOptions.PreserveWhitespace);
        System.Xml.Linq.XNamespace ns = "http://www.microsoft.com/networking/WLAN/profile/v1";
        Assert.Equal(ssid, document.Root!.Element(ns + "name")!.Value);
        Assert.Equal(Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(ssid)), document.Descendants(ns + "hex").Single().Value);
        Assert.Equal(password, document.Descendants(ns + "keyMaterial").Single().Value);
    }

    [Fact]
    public void Build_WhenPersonalWifiRequested_WritesProfileWithPlaintextKeyMaterial()
    {
        string xml = WifiProfileXmlBuilder.Build(
            "Foundry WiFi",
            NetworkConfigurationValidator.WifiSecurityPersonal,
            "ValidPassphrase123");

        Assert.Contains("<name>Foundry WiFi</name>", xml, StringComparison.Ordinal);
        Assert.Contains("<authentication>WPA3SAE</authentication>", xml, StringComparison.Ordinal);
        Assert.Contains("<protected>false</protected>", xml, StringComparison.Ordinal);
        Assert.Contains("<keyMaterial>ValidPassphrase123</keyMaterial>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WhenOpenWifiRequested_DoesNotWriteSharedKey()
    {
        string xml = WifiProfileXmlBuilder.Build(
            "Guest WiFi",
            NetworkConfigurationValidator.WifiSecurityOpen,
            passphraseValue: null);

        Assert.Contains("<authentication>open</authentication>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<sharedKey>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WhenOweWifiRequested_UsesOweAuthentication()
    {
        string xml = WifiProfileXmlBuilder.Build(
            "Guest WiFi",
            NetworkConfigurationValidator.WifiSecurityOwe,
            passphraseValue: null);

        Assert.Contains("<authentication>OWE</authentication>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<sharedKey>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WhenValuesRequireEscaping_WritesEscapedXml()
    {
        string xml = WifiProfileXmlBuilder.Build(
            "Foundry & WiFi",
            NetworkConfigurationValidator.WifiSecurityPersonal,
            "Valid&Passphrase123");

        Assert.Contains("<name>Foundry &amp; WiFi</name>", xml, StringComparison.Ordinal);
        Assert.Contains("<keyMaterial>Valid&amp;Passphrase123</keyMaterial>", xml, StringComparison.Ordinal);
    }
}
