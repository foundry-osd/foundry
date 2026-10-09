// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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

    [Theory]
    [InlineData("HP")]
    [InlineData("Unknown")]
    public void SelectBest_WhenNoCandidateMatchesDeviceModel_ReturnsNoDriverPack(string manufacturer)
    {
        var service = new DriverPackSelectionService(NullLogger<DriverPackSelectionService>.Instance);
        HardwareProfile hardware = new()
        {
            Manufacturer = manufacturer,
            Model = "EliteBook 845",
            Product = "EliteBook 845"
        };
        OperatingSystemCatalogItem operatingSystem = new()
        {
            WindowsRelease = "11",
            ReleaseId = "25H2",
            Architecture = "x64"
        };

        DriverPackCatalogItem hpOlder = CreateCatalogItem(
            id: "hp-older",
            manufacturer: "HP",
            releaseId: "25H2",
            architecture: "x64",
            releaseDate: new DateTimeOffset(2026, 01, 01, 0, 0, 0, TimeSpan.Zero),
            modelNames: ["EliteBook 840"]);

        DriverPackCatalogItem hpNewer = CreateCatalogItem(
            id: "hp-newer",
            manufacturer: "HP",
            releaseId: "25H2",
            architecture: "x64",
            releaseDate: new DateTimeOffset(2026, 02, 01, 0, 0, 0, TimeSpan.Zero),
            modelNames: ["ProBook"]);

        DriverPackCatalogItem dell = CreateCatalogItem(
            id: "dell",
            manufacturer: "Dell",
            releaseId: "25H2",
            architecture: "x64",
            releaseDate: new DateTimeOffset(2026, 03, 01, 0, 0, 0, TimeSpan.Zero),
            modelNames: ["Latitude 5450"]);

        DriverPackSelectionResult result = service.SelectBest([hpOlder, hpNewer, dell], hardware, operatingSystem);

        Assert.Null(result.DriverPack);
        Assert.Equal("No driver pack matches the device model.", result.SelectionReason);
    }

    [Theory]
    [InlineData("25H2")]
    [InlineData("26H2")]
    public void SelectBest_WhenTargetReleaseIsUnavailable_PrefersNewestCompatibleExactModelRelease(string targetRelease)
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
            ReleaseId = targetRelease,
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
        DriverPackCatalogItem win11_24H2 = CreateCatalogItem(
            id: "24h2",
            manufacturer: "Lenovo",
            releaseId: "24H2",
            architecture: "x64",
            releaseDate: catalogDate,
            modelNames: ["ThinkPad X13 Yoga Gen 3 Type 21AW 21AX"]);

        DriverPackSelectionResult result = service.SelectBest([win11_21H2, win11_22H2, win11_24H2], hardware, operatingSystem);

        Assert.Equal("24h2", result.DriverPack?.Id);
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
            id: "exact-24h2",
            manufacturer: "Lenovo",
            releaseId: "24H2",
            architecture: "x64",
            releaseDate: new DateTimeOffset(2024, 06, 13, 0, 0, 0, TimeSpan.Zero),
            modelNames: ["ThinkPad X13 Yoga Gen 3 Type 21AW 21AX"]);
        DriverPackCatalogItem otherModel = CreateCatalogItem(
            id: "other-25h2",
            manufacturer: "Lenovo",
            releaseId: "25H2",
            architecture: "x64",
            releaseDate: new DateTimeOffset(2025, 01, 01, 0, 0, 0, TimeSpan.Zero),
            modelNames: ["ThinkPad T14 Gen 5"]);

        DriverPackSelectionResult result = service.SelectBest([exactModel, otherModel], hardware, operatingSystem);

        Assert.Equal("exact-24h2", result.DriverPack?.Id);
        Assert.Equal("Matched by hardware model/product and compatible OS release.", result.SelectionReason);
    }

    [Fact]
    public void SelectBest_WhenLenovoModelsShareMarketingName_PrefersMatchingMachineType()
    {
        var service = new DriverPackSelectionService(NullLogger<DriverPackSelectionService>.Instance);
        HardwareProfile hardware = new()
        {
            Manufacturer = "Lenovo",
            Model = "21Y6000JMX",
            Product = "ThinkPad E14 Gen 8"
        };
        OperatingSystemCatalogItem operatingSystem = new()
        {
            WindowsRelease = "11",
            ReleaseId = "25H2",
            Architecture = "x64"
        };

        DriverPackCatalogItem newerWrongType = CreateCatalogItem(
            id: "21y2-21y3",
            manufacturer: "Lenovo",
            releaseId: "25H2",
            architecture: "x64",
            releaseDate: new DateTimeOffset(2026, 05, 19, 0, 0, 0, TimeSpan.Zero),
            modelNames: ["ThinkPad E14 Gen 8 Type 21Y2 21Y3"],
            systemIds: ["21Y2", "21Y3"]);
        DriverPackCatalogItem olderMatchingType = CreateCatalogItem(
            id: "21y6-21y7",
            manufacturer: "Lenovo",
            releaseId: "25H2",
            architecture: "x64",
            releaseDate: new DateTimeOffset(2026, 04, 28, 0, 0, 0, TimeSpan.Zero),
            modelNames: ["ThinkPad E14 Gen 8 Type 21Y6 21Y7"],
            systemIds: ["21Y6", "21Y7"]);

        DriverPackSelectionResult result = service.SelectBest(
            [newerWrongType, olderMatchingType],
            hardware,
            operatingSystem);

        Assert.Equal("21y6-21y7", result.DriverPack?.Id);
    }

    private static DriverPackCatalogItem CreateCatalogItem(
        string id,
        string manufacturer,
        string releaseId,
        string architecture,
        DateTimeOffset releaseDate,
        IReadOnlyList<string> modelNames,
        IReadOnlyList<string>? systemIds = null)
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
            ModelNames = modelNames,
            SystemIds = systemIds ?? []
        };
    }
}
