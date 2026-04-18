using Foundry.Deploy.Models;
using Foundry.Deploy.Services.DriverPacks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DriverPackSelectionServiceTests
{
    [Fact]
    public void SelectBest_WhenExactModelExists_PrefersItOverNewerGenericCandidate()
    {
        var service = new DriverPackSelectionService(NullLogger<DriverPackSelectionService>.Instance);
        HardwareProfile hardware = new()
        {
            Manufacturer = "Dell Inc.",
            Model = "Latitude 5450",
            Product = "Latitude 5450"
        };
        OperatingSystemCatalogItem operatingSystem = new()
        {
            WindowsRelease = "11",
            ReleaseId = "24H2",
            Architecture = "amd64"
        };

        DriverPackCatalogItem olderExactMatch = CreateCatalogItem(
            id: "exact",
            manufacturer: "Dell",
            releaseId: "24H2",
            architecture: "x64",
            releaseDate: new DateTimeOffset(2026, 03, 01, 0, 0, 0, TimeSpan.Zero),
            modelNames: ["Latitude 5450"]);

        DriverPackCatalogItem newerGeneric = CreateCatalogItem(
            id: "generic",
            manufacturer: "Dell",
            releaseId: "24H2",
            architecture: "x64",
            releaseDate: new DateTimeOffset(2026, 04, 01, 0, 0, 0, TimeSpan.Zero),
            modelNames: ["OptiPlex"]);

        DriverPackSelectionResult result = service.SelectBest([olderExactMatch, newerGeneric], hardware, operatingSystem);

        Assert.Equal("exact", result.DriverPack?.Id);
        Assert.Equal("Matched by hardware model/product and latest release date.", result.SelectionReason);
    }

    [Fact]
    public void SelectBest_WhenNoExactModelExists_FallsBackToNewestManufacturerCandidate()
    {
        var service = new DriverPackSelectionService(NullLogger<DriverPackSelectionService>.Instance);
        HardwareProfile hardware = new()
        {
            Manufacturer = "HP",
            Model = "EliteBook 845",
            Product = "EliteBook 845"
        };
        OperatingSystemCatalogItem operatingSystem = new()
        {
            WindowsRelease = "11",
            ReleaseId = "25H2",
            Architecture = "x64"
        };

        DriverPackCatalogItem olderCandidate = CreateCatalogItem(
            id: "older",
            manufacturer: "HP",
            releaseId: "25H2",
            architecture: "x64",
            releaseDate: new DateTimeOffset(2026, 01, 01, 0, 0, 0, TimeSpan.Zero),
            modelNames: ["EliteBook 840"]);

        DriverPackCatalogItem newerCandidate = CreateCatalogItem(
            id: "newer",
            manufacturer: "HP",
            releaseId: "25H2",
            architecture: "x64",
            releaseDate: new DateTimeOffset(2026, 02, 01, 0, 0, 0, TimeSpan.Zero),
            modelNames: ["ProBook"]);

        DriverPackSelectionResult result = service.SelectBest([olderCandidate, newerCandidate], hardware, operatingSystem);

        Assert.Equal("newer", result.DriverPack?.Id);
        Assert.Equal("No model exact match; selected newest manufacturer candidate.", result.SelectionReason);
    }

    private static DriverPackCatalogItem CreateCatalogItem(
        string id,
        string manufacturer,
        string releaseId,
        string architecture,
        DateTimeOffset releaseDate,
        IReadOnlyList<string> modelNames)
    {
        return new DriverPackCatalogItem
        {
            Id = id,
            Manufacturer = manufacturer,
            Name = $"{manufacturer} {releaseId}",
            FileName = "driverpack.cab",
            DownloadUrl = "https://example.test/driverpack.cab",
            OsName = "Windows 11",
            OsReleaseId = releaseId,
            OsArchitecture = architecture,
            ReleaseDate = releaseDate,
            ModelNames = modelNames
        };
    }
}
