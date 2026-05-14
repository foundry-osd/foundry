using Foundry.Deploy.Models;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DriverPackSelectionViewModelTests
{
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
        viewModel.ApplyCatalog(
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

    private static DriverPackCatalogItem CreateCatalogItem(
        string id,
        string releaseId,
        DateTimeOffset releaseDate)
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
            ModelNames = ["ThinkPad X13 Yoga Gen 3 Type 21AW 21AX"]
        };
    }
}
