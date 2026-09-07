// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Networking;

namespace Foundry.Utilities.Tests.Networking;

public sealed class WlanProfileReaderTests
{
    [Theory]
    [InlineData("<SSIDConfig><SSID><name>Not a profile</name></SSID></SSIDConfig>")]
    [InlineData("<name>First</name><name>Second</name>")]
    [InlineData("<name><nested>Not text</nested></name>")]
    public void TryReadName_RejectsMissingDuplicateOrStructuredDirectName(string content)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.CreateProfile($"<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">{content}</WLANProfile>");
        Assert.Null(WlanProfileReader.TryReadName(path));
    }

    [Theory]
    [InlineData("<authentication>WPA3ENT</authentication>")]
    [InlineData("<MSM><security><authEncryption><authentication>WPA3ENT</authentication><authentication>WPA2</authentication></authEncryption></security></MSM>")]
    [InlineData("<MSM/><MSM><security><authEncryption><authentication>WPA3ENT</authentication></authEncryption></security></MSM>")]
    public void TryReadAuthentication_RejectsWrongPathOrDuplicateNodes(string content)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.CreateProfile($"<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">{content}</WLANProfile>");
        Assert.Null(WlanProfileReader.TryReadAuthentication(path));
    }

    [Theory]
    [InlineData("<SSIDConfig/><SSIDConfig><SSID><hex>41</hex></SSID></SSIDConfig>")]
    [InlineData("<SSIDConfig><SSID><hex>41</hex></SSID><SSID><hex>42</hex></SSID></SSIDConfig>")]
    [InlineData("<SSIDConfig><SSID><hex>41</hex><hex>42</hex></SSID></SSIDConfig>")]
    [InlineData("<SSIDConfig><SSID><name>A</name><name>B</name></SSID></SSIDConfig>")]
    [InlineData("<SSIDConfig><SSID><hex>41</hex><name>A</name><name>B</name></SSID></SSIDConfig>")]
    public void TryReadSsidHex_RejectsDuplicateNodes(string content)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.CreateProfile($"<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">{content}</WLANProfile>");
        Assert.Null(WlanProfileReader.TryReadSsidHex(path));
    }

    [Fact]
    public void Readers_RejectWrongRootAndOversizedDocuments()
    {
        using var directory = new TemporaryDirectory();
        foreach (string xml in new[]
        {
            "<Wrong xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\"><name>Wrong</name><MSM><security><authEncryption><authentication>WPA3ENT</authentication></authEncryption></security></MSM></Wrong>",
            "<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\"><name>Valid</name><MSM><security><authEncryption><authentication>WPA3ENT</authentication></authEncryption></security></MSM><SSIDConfig><SSID><hex>41</hex></SSID></SSIDConfig><!--" + new string('x', 1024 * 1024) + "--></WLANProfile>"
        })
        {
            string path = directory.CreateProfile(xml);
            Assert.Null(WlanProfileReader.TryReadName(path));
            Assert.Null(WlanProfileReader.TryReadAuthentication(path));
            Assert.Null(WlanProfileReader.TryReadSsidHex(path));
        }
    }

    [Theory]
    [InlineData("<hex>ff</hex><name>misleading</name>", "FF")]
    [InlineData("<name> Lab </name>", "204C616220")]
    [InlineData("<name>   </name>", "202020")]
    [InlineData("<hex>GG</hex><name>Lab</name>", null)]
    [InlineData("<hex></hex><name>Lab</name>", null)]
    [InlineData("<name>12345678901234567890123456789012345</name>", null)]
    public void TryReadSsidHex_UsesSsidIdentityInsteadOfProfileDisplayName(string identity, string? expected)
    {
        using var directory = new TemporaryDirectory();
        string path = directory.CreateProfile($"<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\"><name>Unrelated profile name</name><SSIDConfig><SSID>{identity}</SSID></SSIDConfig></WLANProfile>");
        Assert.Equal(expected, WlanProfileReader.TryReadSsidHex(path));
    }

    [Fact]
    public void TryReadName_PreservesAllSpaceProfileName()
    {
        using var directory = new TemporaryDirectory();
        Assert.Equal("   ", WlanProfileReader.TryReadName(directory.CreateProfile(
            "<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\"><name>   </name></WLANProfile>")));
    }

    [Fact]
    public void TryReadName_WhenProfileContainsEntity_ReturnsExactDecodedValue()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string profilePath = temporaryDirectory.CreateProfile(
            """
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
              <name>  Corp &amp; Guest  </name>
            </WLANProfile>
            """);

        string? name = WlanProfileReader.TryReadName(profilePath);

        Assert.Equal("  Corp & Guest  ", name);
    }

    [Fact]
    public void TryReadAuthentication_WhenFirstValueIsBlank_ReturnsFirstNonBlankValue()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string profilePath = temporaryDirectory.CreateProfile(
            """
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
              <authentication>  </authentication>
              <MSM>
                <security>
                  <authEncryption>
                    <authentication>  WPA3ENT  </authentication>
                  </authEncryption>
                </security>
              </MSM>
            </WLANProfile>
            """);

        string? authentication = WlanProfileReader.TryReadAuthentication(profilePath);

        Assert.Equal("WPA3ENT", authentication);
    }

    [Fact]
    public void TryReadName_WhenElementIsMissing_ReturnsNull()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string profilePath = temporaryDirectory.CreateProfile(
            """
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1" />
            """);

        string? name = WlanProfileReader.TryReadName(profilePath);

        Assert.Null(name);
    }

    [Fact]
    public void TryReadName_WhenPathIsBlank_ReturnsNull()
    {
        Assert.Null(WlanProfileReader.TryReadName("  "));
    }

    [Fact]
    public void TryReadAuthentication_WhenFileIsMissing_ReturnsNull()
    {
        string profilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");

        string? authentication = WlanProfileReader.TryReadAuthentication(profilePath);

        Assert.Null(authentication);
    }

    [Fact]
    public void TryReadAuthentication_WhenXmlIsMalformed_ReturnsNull()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string profilePath = temporaryDirectory.CreateProfile("<WLANProfile>");

        string? authentication = WlanProfileReader.TryReadAuthentication(profilePath);

        Assert.Null(authentication);
    }

    [Fact]
    public void TryReadName_WhenNamespaceDoesNotMatch_ReturnsNull()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string profilePath = temporaryDirectory.CreateProfile(
            """
            <WLANProfile>
              <name>Corp WiFi</name>
            </WLANProfile>
            """);

        string? name = WlanProfileReader.TryReadName(profilePath);

        Assert.Null(name);
    }

    [Fact]
    public void TryReadName_WhenDocumentContainsDtd_ReturnsNull()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string profilePath = temporaryDirectory.CreateProfile(
            """
            <!DOCTYPE WLANProfile [<!ENTITY profileName "Corp WiFi">]>
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
              <name>&profileName;</name>
            </WLANProfile>
            """);

        string? name = WlanProfileReader.TryReadName(profilePath);

        Assert.Null(name);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Foundry.Utilities.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateProfile(string xml)
        {
            string profilePath = System.IO.Path.Combine(Path, "wifi.xml");
            File.WriteAllText(profilePath, xml);
            return profilePath;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
