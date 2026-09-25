// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.DriverPacks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class CustomImageSelectionTests
{
    [Theory]
    [InlineData(22631, "23H2")]
    [InlineData(26100, "24H2")]
    [InlineData(26200, "25H2")]
    public void Selection_RecognizedClientBuildUsesCatalogDriverRelease(int build, string release)
    {
        var selection = CreateSelection(new CustomImageIndex { Index = 1, Build = build, ProductType = "WinNT" });

        Assert.Equal("11", selection.WindowsRelease);
        Assert.Equal(release, selection.ReleaseId);
        Assert.Equal(release, MicrosoftUpdateCatalogSupport.BuildReleaseSearchOrder(selection.ReleaseId)[0]);
    }

    [Theory]
    [InlineData("ServerNT", 26100)]
    [InlineData("LanmanNT", 26100)]
    [InlineData("", 26100)]
    [InlineData("Unknown", 26100)]
    [InlineData("WinNT", 99999)]
    public void Selection_ServerOrUnknownMetadataDoesNotInheritClientDriverRelease(string productType, int build)
    {
        var selection = CreateSelection(new CustomImageIndex { Index = 1, Build = build, ProductType = productType });

        Assert.Empty(selection.WindowsRelease);
        Assert.Empty(selection.ReleaseId);
        Assert.Equal(build, selection.BuildMajor);
    }

    [Theory]
    [InlineData(true, "24H2")]
    [InlineData(false, "23H2")]
    public void Selection_Custom24H2UsesSameDriverPreferenceAndFallbackAsCatalog(bool hasExactRelease, string expectedRelease)
    {
        var service = new DriverPackSelectionService(NullLogger<DriverPackSelectionService>.Instance);
        var hardware = new HardwareProfile { Manufacturer = "Dell", Model = "Latitude 5450", Product = "Latitude 5450" };
        var custom = CreateSelection(new CustomImageIndex { Index = 2, Build = 26100, ProductType = "WinNT", Architecture = "x64" });
        var catalog = new OperatingSystemCatalogItem { WindowsRelease = "11", ReleaseId = "24H2", Architecture = "x64" };
        string[] releases = hasExactRelease ? ["25H2", "24H2", "23H2"] : ["25H2", "23H2"];
        DriverPackCatalogItem[] packs = releases.Select(release => new DriverPackCatalogItem
        {
            Id = release,
            Name = "Dell " + release,
            Manufacturer = "Dell",
            OsName = "Windows 11",
            OsReleaseId = release,
            OsArchitecture = "x64",
            ModelNames = ["Latitude 5450"],
            ReleaseDate = release == "25H2" ? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) : new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
        }).ToArray();

        Assert.Equal(expectedRelease, service.SelectBest(packs, hardware, catalog).DriverPack?.Id);
        Assert.Equal(expectedRelease, service.SelectBest(packs, hardware, custom).DriverPack?.Id);
    }

    [Fact]
    public void Selection_PreservesUnknownMetadataAndExactIndexWithoutCatalogDefaults()
    {
        var index = new CustomImageIndex { Index = 9, Name = "Custom", Architecture = "x86", EditionId = "UnknownEdition", ExpandedSizeBytes = 1024 };
        var selection = new CustomImageSelection(new CustomImageAsset { Id = "manual", DisplayName = "Image", ImagePath = @"D:\Cache\OperatingSystems\Custom\image.wim", VolumeRoot = @"D:\" }, index);
        Assert.Equal(9, selection.Index.Index);
        Assert.Equal("x86", selection.Architecture);
        Assert.Equal("UnknownEdition", selection.Edition);
        Assert.Empty(selection.ReleaseId);
        Assert.Empty(selection.LicenseChannel);
        Assert.Empty(selection.WindowsRelease);
    }

    private static CustomImageSelection CreateSelection(CustomImageIndex index) => new(
        new CustomImageAsset { Id = "custom", DisplayName = "Custom image", ImagePath = @"D:\Cache\OperatingSystems\Custom\image.wim", VolumeRoot = @"D:\" }, index);
}
