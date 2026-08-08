// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Networking;

namespace Foundry.Utilities.Tests.Networking;

public sealed class WlanProfileReaderTests
{
    [Fact]
    public void TryReadName_WhenProfileContainsEntity_ReturnsTrimmedDecodedValue()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        string profilePath = temporaryDirectory.CreateProfile(
            """
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
              <name>  Corp &amp; Guest  </name>
            </WLANProfile>
            """);

        string? name = WlanProfileReader.TryReadName(profilePath);

        Assert.Equal("Corp & Guest", name);
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
