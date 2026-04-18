using Foundry.Deploy.Models;
using Foundry.Deploy.Services.DriverPacks;

namespace Foundry.Deploy.Tests;

public sealed class MicrosoftUpdateCatalogSupportTests
{
    [Fact]
    public void BuildReleaseSearchOrder_PutsSupportedTargetReleaseFirstWithoutDuplicates()
    {
        string[] order = MicrosoftUpdateCatalogSupport.BuildReleaseSearchOrder("24H2");

        Assert.Equal(["24H2", "25H2", "23H2"], order);
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
    public void SelectPreferredCabUrl_PrefersExactArchitectureMatch()
    {
        string? url = MicrosoftUpdateCatalogSupport.SelectPreferredCabUrl(
            [
                "https://example.test/driver-x86.cab",
                "https://example.test/driver-amd64.cab",
                "https://example.test/readme.txt"
            ],
            "x64");

        Assert.Equal("https://example.test/driver-amd64.cab", url);
    }

    [Fact]
    public void SelectPreferredCabUrl_WhenNoExactMatch_FallsBackToCompatibleCab()
    {
        string? url = MicrosoftUpdateCatalogSupport.SelectPreferredCabUrl(
            [
                "https://example.test/driver-generic.cab",
                "https://example.test/driver-arm64.cab"
            ],
            "x64");

        Assert.Equal("https://example.test/driver-generic.cab", url);
    }
}
