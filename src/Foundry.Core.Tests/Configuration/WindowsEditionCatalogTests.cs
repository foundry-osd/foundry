// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Tests.Configuration;

using Foundry.Core.Models.Configuration;

public sealed class WindowsEditionCatalogTests
{
    [Theory]
    [InlineData("Home", "RET")]
    [InlineData("Enterprise", "VOL")]
    [InlineData("Pro", "RET", "VOL")]
    public void GetCompatibleLicenseChannels_ReturnsChannelsSupportedBySelectedEditions(
        string edition,
        params string[] expectedChannels)
    {
        IReadOnlyList<string> channels = WindowsEditionCatalog.GetCompatibleLicenseChannels([edition]);

        Assert.Equal(expectedChannels, channels);
    }

    [Fact]
    public void GetCompatibleLicenseChannels_WhenHomeAndEnterpriseAreSelected_ReturnsBothChannels()
    {
        IReadOnlyList<string> channels = WindowsEditionCatalog.GetCompatibleLicenseChannels(["Home", "Enterprise"]);

        Assert.Equal(["RET", "VOL"], channels);
    }

    [Theory]
    [InlineData(new[] { "Home" }, new[] { "RET" })]
    [InlineData(new[] { "Enterprise" }, new[] { "VOL" })]
    [InlineData(new[] { "Pro" }, new string[0])]
    [InlineData(new[] { "Home", "Enterprise" }, new[] { "RET", "VOL" })]
    public void GetRequiredLicenseChannels_ReturnsChannelsRequiredBySelectedEditions(
        string[] editions,
        string[] expectedChannels)
    {
        IReadOnlyList<string> channels = WindowsEditionCatalog.GetRequiredLicenseChannels(editions);

        Assert.Equal(expectedChannels, channels);
    }
}
