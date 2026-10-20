// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Services.DriverPacks;

namespace Foundry.Deploy.Tests;

public sealed class MicrosoftUpdateCatalogSupportTests
{
    [Fact]
    public void BuildReleaseSearchOrder_PutsSupportedTargetReleaseFirstWithoutDuplicates()
    {
        string[] order = MicrosoftUpdateCatalogSupport.BuildReleaseSearchOrder("24H2");

        Assert.Equal(["24H2", "26H2", "25H2"], order);
    }

    [Fact]
    public void TryExtractDriverSearchHardwareId_UsesDeviceIdBeforeHardwareIds()
    {
        var device = new PnpDeviceInfo
        {
            DeviceId = @"PCI\VEN_8086&DEV_15B7&SUBSYS_00000000",
            HardwareIds = [@"PCI\VEN_1234&DEV_5678"]
        };

        string? hardwareId = MicrosoftUpdateCatalogSupport.TryExtractDriverSearchHardwareId(device);

        Assert.Equal("VEN_8086&DEV_15B7", hardwareId);
    }

    [Fact]
    public void BuildSearchQuery_JoinsOnlyNonEmptySegments()
    {
        string query = MicrosoftUpdateCatalogSupport.BuildSearchQuery(" Surface ", "", "24H2", " x64 ");

        Assert.Equal("Surface+24H2+x64", query);
    }

    [Fact]
    public void SelectPreferredCab_PrefersExactArchitectureMatch()
    {
        MicrosoftUpdateCatalogDownload? download = MicrosoftUpdateCatalogSupport.SelectPreferredCab(
            [
                CreateDownload("https://example.test/driver-x86.cab"),
                CreateDownload("https://example.test/driver-amd64.cab"),
                CreateDownload("https://example.test/readme.txt")
            ],
            "x64");

        Assert.Equal("https://example.test/driver-amd64.cab", download?.DownloadUrl);
    }

    [Fact]
    public void SelectPreferredCab_WhenNoExactMatch_FallsBackToCompatibleCab()
    {
        MicrosoftUpdateCatalogDownload? download = MicrosoftUpdateCatalogSupport.SelectPreferredCab(
            [
                CreateDownload("https://example.test/driver-generic.cab"),
                CreateDownload("https://example.test/driver-arm64.cab")
            ],
            "x64");

        Assert.Equal("https://example.test/driver-generic.cab", download?.DownloadUrl);
    }

    [Theory]
    [InlineData("arm64", "https://example.test/driver-amd64.cab", "https://example.test/driver-arm64.cab", "https://example.test/driver-arm64.cab")]
    [InlineData("ARM64", "https://example.test/driver-x64.cab", "https://example.test/driver-generic.cab", "https://example.test/driver-generic.cab")]
    [InlineData("x64", "https://example.test/driver-arm64.cab", "https://example.test/driver-amd64.cab", "https://example.test/driver-amd64.cab")]
    public void SelectPreferredCab_WhenCompatibleCabFollowsConflictingCab_SelectsCompatibleCab(
        string targetArchitecture,
        string firstUrl,
        string secondUrl,
        string expectedUrl)
    {
        MicrosoftUpdateCatalogDownload? download = MicrosoftUpdateCatalogSupport.SelectPreferredCab(
            [CreateDownload(firstUrl), CreateDownload(secondUrl)],
            targetArchitecture);

        Assert.Equal(expectedUrl, download?.DownloadUrl);
    }

    [Theory]
    [InlineData("arm64", "https://example.test/driver-amd64.cab", "https://example.test/driver-x86.cab")]
    [InlineData("x64", "https://example.test/driver-arm64.cab", "https://example.test/driver-x86.cab")]
    [InlineData("x86", "https://example.test/driver-arm64.cab", "https://example.test/driver-x64.cab")]
    public void SelectPreferredCab_WhenEveryCabTargetsAnotherArchitecture_SelectsNothing(
        string targetArchitecture,
        string firstUrl,
        string secondUrl)
    {
        MicrosoftUpdateCatalogDownload[] downloads = [CreateDownload(firstUrl), CreateDownload(secondUrl)];

        Assert.Null(MicrosoftUpdateCatalogSupport.SelectPreferredCab(downloads, targetArchitecture));
        Assert.True(MicrosoftUpdateCatalogSupport.HasOnlyConflictingCabs(downloads, targetArchitecture));
    }

    [Fact]
    public void SelectPreferredCab_WhenArchitectureColumnConflicts_SelectsNothing()
    {
        var download = new MicrosoftUpdateCatalogDownload
        {
            DownloadUrl = "https://example.test/driver.cab",
            FileName = "driver.cab",
            Architectures = "AMD64"
        };

        Assert.Null(MicrosoftUpdateCatalogSupport.SelectPreferredCab([download], "arm64"));
    }

    [Fact]
    public void SelectPreferredCab_WhenTargetArchitectureIsEmpty_SelectsFirstCab()
    {
        MicrosoftUpdateCatalogDownload? download = MicrosoftUpdateCatalogSupport.SelectPreferredCab(
            [CreateDownload("https://example.test/driver-arm64.cab"), CreateDownload("https://example.test/driver-amd64.cab")],
            "");

        Assert.Equal("https://example.test/driver-arm64.cab", download?.DownloadUrl);
    }

    [Fact]
    public void HasOnlyConflictingCabs_WhenNoCabIsOffered_ReturnsFalse()
    {
        Assert.False(MicrosoftUpdateCatalogSupport.HasOnlyConflictingCabs([CreateDownload("https://example.test/readme-x64.txt")], "arm64"));
    }

    [Fact]
    public void SelectPreferredUpdate_PrefersNewestResultWithoutConflictingArchitecture()
    {
        MicrosoftUpdateCatalogUpdate? update = MicrosoftUpdateCatalogSupport.SelectPreferredUpdate(
            [
                CreateUpdate("newest-x64", "Vendor - Net - 2.0 (x64)"),
                CreateUpdate("generic", "Vendor - Net - 1.5"),
                CreateUpdate("arm64", "Vendor - Net - 1.0 (ARM64)")
            ],
            "arm64");

        Assert.Equal("generic", update?.UpdateId);
    }

    [Fact]
    public void SelectPreferredUpdate_WhenEveryTitleConflicts_KeepsNewestResult()
    {
        MicrosoftUpdateCatalogUpdate? update = MicrosoftUpdateCatalogSupport.SelectPreferredUpdate(
            [CreateUpdate("newest-x64", "Vendor - Net - 2.0 (x64)"), CreateUpdate("x86", "Vendor - Net - 1.0 (x86)")],
            "arm64");

        Assert.Equal("newest-x64", update?.UpdateId);
    }

    [Fact]
    public void ResolvePreferredHash_PrefersSha256OverSha1()
    {
        var download = new MicrosoftUpdateCatalogDownload
        {
            DownloadUrl = "https://example.test/driver.cab",
            FileName = "driver.cab",
            Sha1 = new string('A', 40),
            Sha256 = new string('B', 64)
        };

        string hash = MicrosoftUpdateCatalogSupport.ResolvePreferredHash(download);

        Assert.Equal(new string('B', 64), hash);
    }

    private static MicrosoftUpdateCatalogDownload CreateDownload(string url)
    {
        return new MicrosoftUpdateCatalogDownload
        {
            DownloadUrl = url,
            FileName = MicrosoftUpdateCatalogSupport.ResolveFileNameFromUrl(url)
        };
    }

    private static MicrosoftUpdateCatalogUpdate CreateUpdate(string updateId, string title)
    {
        return new MicrosoftUpdateCatalogUpdate
        {
            UpdateId = updateId,
            Title = title
        };
    }
}
