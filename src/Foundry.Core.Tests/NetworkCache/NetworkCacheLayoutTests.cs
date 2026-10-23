// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.NetworkCache;

namespace Foundry.Core.Tests.NetworkCache;

public sealed class NetworkCacheLayoutTests
{
    [Fact]
    public void DownloadKey_WithHash_IsLowerHex()
    {
        Assert.Equal("abcd01", NetworkCacheLayout.DownloadKey("https://example.test/a.esd", " ABCD01 ", 10));
    }

    [Fact]
    public void DownloadKey_Hashless_IncludesSize()
    {
        string small = NetworkCacheLayout.DownloadKey("https://example.test/a.cab", null, 10);
        string large = NetworkCacheLayout.DownloadKey("https://example.test/a.cab", null, 11);

        Assert.StartsWith("u-", small);
        Assert.EndsWith("-10", small);
        Assert.NotEqual(small, large);
    }

    [Fact]
    public void DownloadKey_Hashless_UnknownSizeUsesZero()
    {
        Assert.EndsWith("-0", NetworkCacheLayout.DownloadKey("https://example.test/a.cab", "", null));
    }

    [Fact]
    public void DownloadKey_Hashless_NormalizesUrlCaseOfHost()
    {
        Assert.Equal(
            NetworkCacheLayout.DownloadKey("https://Example.TEST/a.cab", null, 1),
            NetworkCacheLayout.DownloadKey("https://example.test/a.cab", null, 1));
    }

    [Fact]
    public void DownloadDirectory_UsesFoundryDownloadsKindKey()
    {
        Assert.Equal(
            @"\\fs01\cache\Foundry\Downloads\OemDriverPack\abcd",
            NetworkCacheLayout.DownloadDirectory(@"\\fs01\cache", "OemDriverPack", "abcd"));
    }

    [Fact]
    public void PartialName_ContainsSession()
    {
        Assert.Equal("image.esd.0123.partial", NetworkCacheLayout.PartialName("image.esd", "0123"));
    }

    [Fact]
    public void CacheableKinds_ExcludesPostInstall()
    {
        Assert.Contains("OperatingSystemImage", NetworkCacheLayout.CacheableKinds);
        Assert.Contains("MicrosoftUpdateCatalogFirmware", NetworkCacheLayout.CacheableKinds);
        Assert.DoesNotContain("PostInstall", NetworkCacheLayout.CacheableKinds);
    }
}
