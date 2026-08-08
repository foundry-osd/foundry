// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Networking;

namespace Foundry.Utilities.Tests.Networking;

public sealed class WindowsUpdateContentUrlTests
{
    [Theory]
    [InlineData(
        "https://dl.delivery.mp.microsoft.com/filestreamingservice/files/source.esd",
        "http://dl.delivery.mp.microsoft.com/filestreamingservice/files/source.esd")]
    [InlineData(
        "https://cdn.dl.delivery.mp.microsoft.com/files/source.esd",
        "http://cdn.dl.delivery.mp.microsoft.com/files/source.esd")]
    [InlineData(
        "HTTPS://DL.DELIVERY.MP.MICROSOFT.COM:443/files/source.esd",
        "http://dl.delivery.mp.microsoft.com/files/source.esd")]
    [InlineData(
        "https://dl.delivery.mp.microsoft.com:8443/files/source.esd",
        "http://dl.delivery.mp.microsoft.com:8443/files/source.esd")]
    public void Normalize_WithHttpsWindowsUpdateContentUrl_UsesHttp(string sourceUrl, string expected)
    {
        Assert.Equal(expected, WindowsUpdateContentUrl.Normalize(sourceUrl));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("http://dl.delivery.mp.microsoft.com/files/source.esd")]
    [InlineData("https://example.test/files/source.esd")]
    [InlineData("https://delivery.mp.microsoft.com/files/source.esd")]
    public void Normalize_WithUnsupportedUrl_ReturnsOriginalValue(string sourceUrl)
    {
        Assert.Equal(sourceUrl, WindowsUpdateContentUrl.Normalize(sourceUrl));
    }
}
