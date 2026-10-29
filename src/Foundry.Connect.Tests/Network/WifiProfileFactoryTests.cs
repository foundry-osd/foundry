// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Xml.Linq;
using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;

namespace Foundry.Connect.Tests.Network;

public sealed class WifiProfileFactoryTests
{
    private static readonly XNamespace V1 = "http://www.microsoft.com/networking/WLAN/profile/v1";
    private static readonly XNamespace V4 = "http://www.microsoft.com/networking/WLAN/profile/v4";
    private static readonly WifiSsid Lab = WifiSsid.FromText("Lab & Co");

    private static XDocument Parse(WifiProfileBuildResult result) => XDocument.Parse(Assert.IsType<WifiProfileDocument>(result.Profile).Xml);

    [Fact]
    public void Build_Open_WritesOpenProfileWithByteHex()
    {
        WifiProfileBuildResult result = WifiProfileFactory.Build(Lab, WifiProfileSecurity.Open, ReadOnlySpan<char>.Empty);
        XDocument xml = Parse(result);

        Assert.Equal("Lab & Co", result.Profile!.Name);
        Assert.Equal("Lab & Co", xml.Root!.Element(V1 + "name")!.Value);
        Assert.Equal(Lab.Hex, xml.Descendants(V1 + "hex").Single().Value);
        Assert.Equal("manual", xml.Root.Element(V1 + "connectionMode")!.Value);
        Assert.Equal("open", xml.Descendants(V1 + "authentication").Single().Value);
        Assert.Equal("none", xml.Descendants(V1 + "encryption").Single().Value);
        Assert.Empty(xml.Descendants(V1 + "sharedKey"));
    }

    [Fact]
    public void Build_Owe_WritesOweAes()
    {
        XDocument xml = Parse(WifiProfileFactory.Build(Lab, WifiProfileSecurity.Owe, ReadOnlySpan<char>.Empty));

        Assert.Equal("OWE", xml.Descendants(V1 + "authentication").Single().Value);
        Assert.Equal("AES", xml.Descendants(V1 + "encryption").Single().Value);
    }

    [Theory]
    [InlineData(WifiProfileSecurity.Wpa2Personal, "WPA2PSK", false)]
    [InlineData(WifiProfileSecurity.Wpa3Personal, "WPA3SAE", false)]
    [InlineData(WifiProfileSecurity.Wpa2Wpa3Personal, "WPA3SAE", true)]
    public void Build_Personal_WritesAuthenticationPerType(WifiProfileSecurity security, string authentication, bool transitionMode)
    {
        XDocument xml = Parse(WifiProfileFactory.Build(Lab, security, "correct horse"));

        Assert.Equal(authentication, xml.Descendants(V1 + "authentication").Single().Value);
        Assert.Equal("AES", xml.Descendants(V1 + "encryption").Single().Value);
        Assert.Equal("passPhrase", xml.Descendants(V1 + "keyType").Single().Value);
        Assert.Equal("false", xml.Descendants(V1 + "protected").Single().Value);
        Assert.Equal(transitionMode, xml.Descendants(V4 + "transitionMode").Any());
    }

    [Fact]
    public void Build_Personal_DoesNotTrimPassphrase()
    {
        XDocument xml = Parse(WifiProfileFactory.Build(Lab, WifiProfileSecurity.Wpa2Personal, "  pass <&> word  "));

        Assert.Equal("  pass <&> word  ", xml.Descendants(V1 + "keyMaterial").Single().Value);
    }

    [Fact]
    public void Build_Wpa2Personal_Accepts64HexAsNetworkKey()
    {
        string key = new('a', 64);
        XDocument xml = Parse(WifiProfileFactory.Build(Lab, WifiProfileSecurity.Wpa2Personal, key));

        Assert.Equal("networkKey", xml.Descendants(V1 + "keyType").Single().Value);
        Assert.Equal(key, xml.Descendants(V1 + "keyMaterial").Single().Value);
    }

    [Theory]
    [InlineData(WifiProfileSecurity.Wpa2Personal, "", WifiProfileError.PassphraseRequired)]
    [InlineData(WifiProfileSecurity.Wpa2Personal, "short", WifiProfileError.PassphraseTooShort)]
    [InlineData(WifiProfileSecurity.Wpa2Personal, "pässwörd-long", WifiProfileError.PassphraseInvalidCharacters)]
    [InlineData(WifiProfileSecurity.Wpa2Personal, "line\nbreak1", WifiProfileError.PassphraseInvalidCharacters)]
    [InlineData(WifiProfileSecurity.Wpa3Personal, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", WifiProfileError.PassphraseTooLong)]
    [InlineData(WifiProfileSecurity.Wpa2Personal, "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", WifiProfileError.PassphraseTooLong)]
    public void Build_Personal_RejectsInvalidPassphrase(WifiProfileSecurity security, string passphrase, WifiProfileError expected)
    {
        WifiProfileBuildResult result = WifiProfileFactory.Build(Lab, security, passphrase);

        Assert.Null(result.Profile);
        Assert.Equal(expected, result.Error);
    }

    [Fact]
    public void Build_Open_IgnoresPassphrase()
    {
        WifiProfileBuildResult result = WifiProfileFactory.Build(Lab, WifiProfileSecurity.Open, "ignored-secret");

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("ignored-secret", result.Profile!.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_SsidWithControlCharacter_StillProducesValidXml()
    {
        WifiSsid ssid = new([0x4C, 0x01, 0x62]);

        WifiProfileBuildResult result = WifiProfileFactory.Build(ssid, WifiProfileSecurity.Open, ReadOnlySpan<char>.Empty);
        XDocument xml = Parse(result);

        Assert.Equal(ssid.Hex, xml.Descendants(V1 + "hex").Single().Value);
        Assert.Equal(result.Profile!.Name, xml.Root!.Element(V1 + "name")!.Value);
    }

    [Fact]
    public void FromXml_ReadsTemplateIdentity()
    {
        const string template = """
            <?xml version="1.0"?>
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
              <name>Corp Profile</name>
              <SSIDConfig>
                <SSID><hex>436F7270</hex><name>Corp</name></SSID>
                <nonBroadcast>true</nonBroadcast>
              </SSIDConfig>
            </WLANProfile>
            """;

        WifiProfileDocument profile = Assert.IsType<WifiProfileDocument>(WifiProfileDocument.FromXml(template));

        Assert.Equal("Corp Profile", profile.Name);
        Assert.Equal(WifiSsid.FromText("Corp"), profile.Ssid);
        Assert.True(profile.IsHidden);
        Assert.Null(WifiProfileDocument.FromXml("<not-a-profile/>"));
    }

    [Fact]
    public void Document_HidesXmlAndCanBeCleared()
    {
        WifiProfileDocument profile = WifiProfileFactory.Build(Lab, WifiProfileSecurity.Wpa2Personal, "correct horse").Profile!;

        Assert.DoesNotContain("correct horse", profile.ToString(), StringComparison.Ordinal);
        profile.Clear();
        Assert.Throws<ObjectDisposedException>(() => profile.Xml);
    }
}
