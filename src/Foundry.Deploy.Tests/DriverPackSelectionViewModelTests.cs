// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DriverPackSelectionViewModelTests
{
    [Fact]
    public void EffectiveSelectionKind_WhenDetectedHardwareIsVirtualMachine_DefaultsToNone()
    {
        var viewModel = new DriverPackSelectionViewModel(
            new DriverPackSelectionService(NullLogger<DriverPackSelectionService>.Instance),
            new LocalizationService(),
            "x64");
        HardwareProfile hardware = new()
        {
            Manufacturer = "Microsoft Corporation",
            Model = "Virtual Machine",
            Product = "Virtual Machine",
            IsVirtualMachine = true
        };
        OperatingSystemCatalogItem operatingSystem = new()
        {
            WindowsRelease = "11",
            ReleaseId = "25H2",
            Architecture = "x64"
        };

        viewModel.UpdateSelectionContext(hardware, operatingSystem, "x64");
        viewModel.ReplaceCatalog([]);

        Assert.Equal(DriverPackSelectionKind.None, viewModel.EffectiveSelectionKind);
    }

    [Fact]
    public void ResolveEffectiveSelection_WhenLenovoPacksShareReleaseDate_SelectsNewestCompatibleRelease()
    {
        var viewModel = new DriverPackSelectionViewModel(
            new DriverPackSelectionService(NullLogger<DriverPackSelectionService>.Instance),
            new LocalizationService(),
            "x64");
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

        viewModel.UpdateSelectionContext(hardware, operatingSystem, "x64");
        viewModel.ReplaceCatalog(
        [
            CreateCatalogItem("21h2", "21H2", catalogDate),
            CreateCatalogItem("22h2", "22H2", catalogDate),
            CreateCatalogItem("23h2", "23H2", catalogDate)
        ]);

        DriverPackCatalogItem? selected = viewModel.ResolveEffectiveSelection();

        Assert.Equal(DriverPackSelectionKind.OemCatalog, viewModel.EffectiveSelectionKind);
        Assert.Equal("ThinkPad X13 Yoga Gen 3 Type 21AW 21AX", viewModel.SelectedDriverPackModel);
        Assert.Contains("23H2", viewModel.SelectedDriverPackVersion, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("23h2", selected?.Id);
    }

    [Fact]
    public void ResolveEffectiveSelection_WhenLenovoModelsShareMarketingName_SelectsMatchingMachineType()
    {
        var viewModel = new DriverPackSelectionViewModel(
            new DriverPackSelectionService(NullLogger<DriverPackSelectionService>.Instance),
            new LocalizationService(),
            "x64");
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

        viewModel.UpdateSelectionContext(hardware, operatingSystem, "x64");
        viewModel.ReplaceCatalog(
        [
            CreateCatalogItem(
                "21y2-21y3",
                "25H2",
                new DateTimeOffset(2026, 05, 19, 0, 0, 0, TimeSpan.Zero),
                "ThinkPad E14 Gen 8 Type 21Y2 21Y3",
                ["21Y2", "21Y3"]),
            CreateCatalogItem(
                "21y6-21y7",
                "25H2",
                new DateTimeOffset(2026, 04, 28, 0, 0, 0, TimeSpan.Zero),
                "ThinkPad E14 Gen 8 Type 21Y6 21Y7",
                ["21Y6", "21Y7"])
        ]);

        DriverPackCatalogItem? selected = viewModel.ResolveEffectiveSelection();

        Assert.Equal("ThinkPad E14 Gen 8 Type 21Y6 21Y7", viewModel.SelectedDriverPackModel);
        Assert.Equal("21y6-21y7", selected?.Id);
    }

    private static DriverPackCatalogItem CreateCatalogItem(
        string id,
        string releaseId,
        DateTimeOffset releaseDate,
        string modelName = "ThinkPad X13 Yoga Gen 3 Type 21AW 21AX",
        IReadOnlyList<string>? systemIds = null)
    {
        return new DriverPackCatalogItem
        {
            Id = id,
            Manufacturer = "Lenovo",
            Name = $"ThinkPad X13 Yoga Gen 3 {releaseId}",
            FileName = $"tp_x13_yoga_g3_w11_{releaseId}.exe",
            DownloadUrl = $"https://example.test/{id}.exe",
            OsName = "Windows 11",
            OsReleaseId = releaseId,
            OsArchitecture = "x64",
            ReleaseDate = releaseDate,
            ModelNames = [modelName],
            SystemIds = systemIds ?? []
        };
    }
}
