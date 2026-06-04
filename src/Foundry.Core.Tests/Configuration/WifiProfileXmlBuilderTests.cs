using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class WifiProfileXmlBuilderTests
{
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
