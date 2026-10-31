// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using Foundry.Utilities.Networking;

namespace Foundry.Utilities.Tests.Networking;

public sealed class WlanProfileReaderTests
{
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
    public void TryReadAuthentication_WhenPathIsBlank_ReturnsNull()
    {
        Assert.Null(WlanProfileReader.TryReadAuthentication("  "));
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
    public void TryReadAuthentication_WhenNamespaceDoesNotMatch_ReturnsNull()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string profilePath = temporaryDirectory.CreateProfile(
            """
            <WLANProfile>
              <authentication>WPA2PSK</authentication>
            </WLANProfile>
            """);

        string? authentication = WlanProfileReader.TryReadAuthentication(profilePath);

        Assert.Null(authentication);
    }

    [Fact]
    public void TryReadAuthentication_WhenDocumentContainsDtd_ReturnsNull()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string profilePath = temporaryDirectory.CreateProfile(
            """
            <!DOCTYPE WLANProfile [<!ENTITY authentication "WPA2PSK">]>
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
              <authentication>&authentication;</authentication>
            </WLANProfile>
            """);

        string? authentication = WlanProfileReader.TryReadAuthentication(profilePath);

        Assert.Null(authentication);
    }

    [Theory]
    [InlineData("""<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1"><MSM><security><sharedKey><keyMaterial>k</keyMaterial></sharedKey></security></MSM></WLANProfile>""", WlanProfileKeyMaterial.Present)]
    [InlineData("""<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1"><MSM><security><authEncryption><authentication>WPA2</authentication></authEncryption></security></MSM></WLANProfile>""", WlanProfileKeyMaterial.Absent)]
    [InlineData("not xml", WlanProfileKeyMaterial.Unreadable)]
    [InlineData("""<!DOCTYPE WLANProfile [<!ENTITY k "secret">]><WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1"><name>&k;</name></WLANProfile>""", WlanProfileKeyMaterial.Unreadable)]
    public void DetectKeyMaterial_ClassifiesTheProfile(string xml, WlanProfileKeyMaterial expected)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        Assert.Equal(expected, WlanProfileReader.DetectKeyMaterial(stream));
    }

    [Fact]
    public void DetectKeyMaterial_HonorsTheDeclaredEncoding()
    {
        // UTF-16 bytes with a BOM and a matching declaration: a byte stream reader decodes them, a UTF-8 text decoder would not.
        byte[] content = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(
            """<?xml version="1.0" encoding="UTF-16"?><WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1"><keyMaterial>k</keyMaterial></WLANProfile>""")];
        using var stream = new MemoryStream(content);

        Assert.Equal(WlanProfileKeyMaterial.Present, WlanProfileReader.DetectKeyMaterial(stream));
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
