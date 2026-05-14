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
        Assert.Equal("Matched by hardware model/product and compatible OS release.", result.SelectionReason);
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
        Assert.Equal("No model exact match; selected newest compatible manufacturer candidate.", result.SelectionReason);
    }

    [Fact]
    public void SelectBest_WhenTargetReleaseIsUnavailable_PrefersNewestCompatibleExactModelRelease()
    {
        var service = new DriverPackSelectionService(NullLogger<DriverPackSelectionService>.Instance);
        HardwareProfile hardware = new()
        {
            Manufacturer = "Lenovo",
            Model = "ThinkPad X13 Yoga Gen 3 Type 21AW 21AX",
            Product = "21AW"
        };
        OperatingSystemCatalogItem operatingSystem = new()
        {
            WindowsRelease = "11",
            ReleaseId = "25H2",
            Architecture = "x64"
        };
        DateTimeOffset catalogDate = new(2024, 06, 13, 0, 0, 0, TimeSpan.Zero);

        DriverPackCatalogItem win11_21H2 = CreateCatalogItem(
            id: "21h2",
            manufacturer: "Lenovo",
            releaseId: "21H2",
            architecture: "x64",
            releaseDate: catalogDate,
            modelNames: ["ThinkPad X13 Yoga Gen 3 Type 21AW 21AX"]);
        DriverPackCatalogItem win11_22H2 = CreateCatalogItem(
            id: "22h2",
            manufacturer: "Lenovo",
            releaseId: "22H2",
            architecture: "x64",
            releaseDate: catalogDate,
            modelNames: ["ThinkPad X13 Yoga Gen 3 Type 21AW 21AX"]);
        DriverPackCatalogItem win11_23H2 = CreateCatalogItem(
            id: "23h2",
            manufacturer: "Lenovo",
            releaseId: "23H2",
            architecture: "x64",
            releaseDate: catalogDate,
            modelNames: ["ThinkPad X13 Yoga Gen 3 Type 21AW 21AX"]);

        DriverPackSelectionResult result = service.SelectBest([win11_21H2, win11_22H2, win11_23H2], hardware, operatingSystem);

        Assert.Equal("23h2", result.DriverPack?.Id);
        Assert.Equal("Matched by hardware model/product and compatible OS release.", result.SelectionReason);
    }

    [Fact]
    public void SelectBest_WhenNewerCompatibleReleaseExistsForDifferentModel_PrefersExactModel()
    {
        var service = new DriverPackSelectionService(NullLogger<DriverPackSelectionService>.Instance);
        HardwareProfile hardware = new()
        {
            Manufacturer = "Lenovo",
            Model = "ThinkPad X13 Yoga Gen 3 Type 21AW 21AX",
            Product = "21AW"
        };
        OperatingSystemCatalogItem operatingSystem = new()
        {
            WindowsRelease = "11",
            ReleaseId = "25H2",
            Architecture = "x64"
        };

        DriverPackCatalogItem exactModel = CreateCatalogItem(
            id: "exact-23h2",
            manufacturer: "Lenovo",
            releaseId: "23H2",
            architecture: "x64",
            releaseDate: new DateTimeOffset(2024, 06, 13, 0, 0, 0, TimeSpan.Zero),
            modelNames: ["ThinkPad X13 Yoga Gen 3 Type 21AW 21AX"]);
        DriverPackCatalogItem otherModel = CreateCatalogItem(
            id: "other-24h2",
            manufacturer: "Lenovo",
            releaseId: "24H2",
            architecture: "x64",
            releaseDate: new DateTimeOffset(2025, 01, 01, 0, 0, 0, TimeSpan.Zero),
            modelNames: ["ThinkPad T14 Gen 5"]);

        DriverPackSelectionResult result = service.SelectBest([exactModel, otherModel], hardware, operatingSystem);

        Assert.Equal("exact-23h2", result.DriverPack?.Id);
        Assert.Equal("Matched by hardware model/product and compatible OS release.", result.SelectionReason);
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
