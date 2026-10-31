// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Drivers;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.ViewModels;
using Foundry.Wpf.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DriverPackSelectionViewModelTests
{
    [Fact]
    public void EffectiveSelectionKind_WhenDetectedHardwareIsVirtualMachine_DefaultsToNone()
    {
        var viewModel = new DriverPackSelectionViewModel(
            new DriverPackSelectionService(NullLogger<DriverPackSelectionService>.Instance),
            new ResourceLocalizationService(LocalizationText.ResourceManager),
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
    public void EffectiveSelectionKind_WhenPhysicalHardwareMatchesNoDriverPackModel_DefaultsToMicrosoftUpdateCatalog()
    {
        var viewModel = new DriverPackSelectionViewModel(
            new DriverPackSelectionService(NullLogger<DriverPackSelectionService>.Instance),
            new ResourceLocalizationService(LocalizationText.ResourceManager),
            "x64");
        HardwareProfile hardware = new()
        {
            Manufacturer = "Lenovo",
            Model = "ThinkPad T14 Gen 5",
            Product = "21ML",
            IsVirtualMachine = false
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
            CreateCatalogItem("x13", "25H2", new DateTimeOffset(2026, 01, 01, 0, 0, 0, TimeSpan.Zero))
        ]);

        Assert.Equal(DriverPackSelectionKind.MicrosoftUpdateCatalog, viewModel.EffectiveSelectionKind);
    }

    [Fact]
    public void ResolveEffectiveSelection_WhenLenovoPacksShareReleaseDate_SelectsNewestCompatibleRelease()
    {
        var viewModel = new DriverPackSelectionViewModel(
            new DriverPackSelectionService(NullLogger<DriverPackSelectionService>.Instance),
            new ResourceLocalizationService(LocalizationText.ResourceManager),
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
            CreateCatalogItem("24h2", "24H2", catalogDate)
        ]);

        DriverPackCatalogItem? selected = viewModel.ResolveEffectiveSelection();

        Assert.Equal(DriverPackSelectionKind.OemCatalog, viewModel.EffectiveSelectionKind);
        Assert.Equal("ThinkPad X13 Yoga Gen 3 Type 21AW 21AX", viewModel.SelectedDriverPackModel);
        Assert.Contains("24H2", viewModel.SelectedDriverPackVersion, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("24h2", selected?.Id);
    }

    [Fact]
    public void ResolveEffectiveSelection_WhenLenovoModelsShareMarketingName_SelectsMatchingMachineType()
    {
        var viewModel = new DriverPackSelectionViewModel(
            new DriverPackSelectionService(NullLogger<DriverPackSelectionService>.Instance),
            new ResourceLocalizationService(LocalizationText.ResourceManager),
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UpdateSelectionContext_CustomImageInspectionAndIndexChangePreserveChosenDriverVersion(bool automaticOemSource)
    {
        using var model = new DriverPackSelectionViewModel(
            new DriverPackSelectionService(NullLogger<DriverPackSelectionService>.Instance), new ResourceLocalizationService(LocalizationText.ResourceManager), "x64");
        var asset = new CustomImageAsset { Id = "image", DisplayName = "Image", ImagePath = @"D:\image.wim", VolumeRoot = @"D:\" };
        var index = new Foundry.Core.Models.Configuration.CustomImageIndex { Index = 1, Architecture = "x64", ProductType = "WinNT", Build = 26200 };
        HardwareProfile? hardware = automaticOemSource
            ? new HardwareProfile { Manufacturer = "Lenovo", Model = "ThinkPad X13 Yoga Gen 3 Type 21AW 21AX" }
            : null;
        model.UpdateSelectionContext(hardware, new CustomImageSelection(asset, index), "x64");
        model.ReplaceCatalog([
            CreateCatalogItem("older", "24H2", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            CreateCatalogItem("newer", "25H2", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
        ]);
        if (!automaticOemSource) model.SelectedDriverPackOption = model.DriverPackOptions.Single(option => option.DisplayName == "Lenovo");
        model.SelectedDriverPackVersion = model.DriverPackVersionOptions.Single(version => version.Contains("24H2", StringComparison.Ordinal));
        string selectedModel = model.SelectedDriverPackModel;
        string selectedVersion = model.SelectedDriverPackVersion;

        model.UpdateSelectionContext(hardware, null, "x64");
        Assert.Equal(selectedVersion, model.SelectedDriverPackVersion);
        model.UpdateSelectionContext(hardware, new CustomImageSelection(asset, index with { Index = 7 }), "x64");

        Assert.Equal(selectedModel, model.SelectedDriverPackModel);
        Assert.Equal(selectedVersion, model.SelectedDriverPackVersion);
        Assert.Equal("older", model.ResolveEffectiveSelection()?.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UpdateSelectionContext_TargetReleaseChanges_SelectsPreferredDriverVersion(bool customImage)
    {
        using var model = new DriverPackSelectionViewModel(
            new DriverPackSelectionService(NullLogger<DriverPackSelectionService>.Instance), new ResourceLocalizationService(LocalizationText.ResourceManager), "x64");
        var asset = new CustomImageAsset { Id = "image", DisplayName = "Image", ImagePath = @"D:\image.wim", VolumeRoot = @"D:\" };
        OperatingSystemMetadata initial = customImage
            ? new CustomImageSelection(asset, new() { Index = 1, Architecture = "x64", ProductType = "WinNT", Build = 26200 })
            : new OperatingSystemCatalogItem { WindowsRelease = "11", ReleaseId = "25H2", Architecture = "x64" };
        OperatingSystemMetadata changed = customImage
            ? new CustomImageSelection(asset, new() { Index = 2, Architecture = "x64", ProductType = "WinNT", Build = 26100 })
            : new OperatingSystemCatalogItem { WindowsRelease = "11", ReleaseId = "24H2", Architecture = "x64" };
        model.UpdateSelectionContext(null, initial, "x64");
        model.ReplaceCatalog([
            CreateCatalogItem("older", "24H2", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            CreateCatalogItem("newer", "25H2", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
        ]);
        model.SelectedDriverPackOption = model.DriverPackOptions.Single(option => option.DisplayName == "Lenovo");
        Assert.Equal("newer", model.ResolveEffectiveSelection()?.Id);

        if (customImage) model.UpdateSelectionContext(null, null, "x64");
        model.UpdateSelectionContext(null, changed, "x64");

        Assert.Equal("older", model.ResolveEffectiveSelection()?.Id);
    }

    [Fact]
    public void UpdateSelectionContext_TargetArchitectureChanges_ReevaluatesChosenDriverVersion()
    {
        using var model = new DriverPackSelectionViewModel(
            new DriverPackSelectionService(NullLogger<DriverPackSelectionService>.Instance), new ResourceLocalizationService(LocalizationText.ResourceManager), "x64");
        var operatingSystem = new OperatingSystemCatalogItem { WindowsRelease = "11", ReleaseId = "25H2", Architecture = "x64" };
        var older = CreateCatalogItem("older", "24H2", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = CreateCatalogItem("newer", "25H2", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        model.UpdateSelectionContext(null, operatingSystem, "x64");
        model.ReplaceCatalog([older, newer, older with { Id = "older-arm64", OsArchitecture = "arm64" }, newer with { Id = "newer-arm64", OsArchitecture = "arm64" }]);
        model.SelectedDriverPackOption = model.DriverPackOptions.Single(option => option.DisplayName == "Lenovo");
        model.SelectedDriverPackVersion = model.DriverPackVersionOptions.Single(version => version.Contains("24H2", StringComparison.Ordinal));

        model.UpdateSelectionContext(null, operatingSystem with { Architecture = "arm64" }, "x64");

        Assert.Equal("newer-arm64", model.ResolveEffectiveSelection()?.Id);
    }

    [Fact]
    public void CustomOptionHiddenWhenCatalogUnavailable()
    {
        using DriverPackSelectionViewModel model = CreatePhysicalLenovoModel();

        model.ConfigureCustomDriverSets(new CustomDriverSetCatalog(CustomDriverSetCatalogStatus.Unavailable, []), "set-a");
        Assert.DoesNotContain(model.DriverPackOptions, option => option.Kind == DriverPackSelectionKind.CustomDriverSet);

        model.ConfigureCustomDriverSets(new CustomDriverSetCatalog(CustomDriverSetCatalogStatus.Available, []), null);
        Assert.DoesNotContain(model.DriverPackOptions, option => option.Kind == DriverPackSelectionKind.CustomDriverSet);
        Assert.Empty(model.CustomDriverSetOptions);
    }

    [Fact]
    public void CustomOptionListedWhenSetsAvailable()
    {
        using DriverPackSelectionViewModel model = CreatePhysicalLenovoModel();

        model.ConfigureCustomDriverSets(CreateAvailableCatalog("set-a", "set-b"), null);

        DriverPackOptionItem custom = Assert.Single(model.DriverPackOptions, option => option.Kind == DriverPackSelectionKind.CustomDriverSet);
        Assert.Equal("custom-driver-set", custom.Key);
        Assert.Equal(["set-a", "set-b"], model.CustomDriverSetOptions.Select(set => set.Id));
        Assert.Equal(DriverPackSelectionKind.MicrosoftUpdateCatalog, model.EffectiveSelectionKind);
        Assert.False(model.IsCustomDriverSourceSelected);
        Assert.False(model.IsCustomDriverSetSelectionEnabled);
        Assert.False(model.HasCustomDriverIntegrityWarning);
    }

    [Fact]
    public void DefaultSetPreselectedOnVirtualMachine()
    {
        using var model = new DriverPackSelectionViewModel(
            new DriverPackSelectionService(NullLogger<DriverPackSelectionService>.Instance), new ResourceLocalizationService(LocalizationText.ResourceManager), "x64");
        model.UpdateSelectionContext(
            new HardwareProfile { Manufacturer = "Microsoft Corporation", Model = "Virtual Machine", IsVirtualMachine = true },
            new OperatingSystemCatalogItem { WindowsRelease = "11", ReleaseId = "25H2", Architecture = "x64" },
            "x64");
        model.ReplaceCatalog([]);

        model.ConfigureCustomDriverSets(CreateAvailableCatalog("set-a", "set-b"), "set-b");

        Assert.Equal(DriverPackSelectionKind.CustomDriverSet, model.EffectiveSelectionKind);
        Assert.True(model.IsCustomDriverSourceSelected);
        Assert.True(model.IsCustomDriverSetSelectionEnabled);
        Assert.Equal("set-b", model.SelectedCustomDriverSet?.Id);
        Assert.Equal("set-b", model.ResolveEffectiveCustomDriverSet()?.Id);
        Assert.True(model.HasValidSelection());
    }

    [Fact]
    public void MissingDefaultFallsBackToCurrentLogicWithoutWarning()
    {
        using DriverPackSelectionViewModel model = CreatePhysicalLenovoModel();

        model.ConfigureCustomDriverSets(new CustomDriverSetCatalog(CustomDriverSetCatalogStatus.Unavailable, []), "set-a");

        Assert.Equal(DriverPackSelectionKind.MicrosoftUpdateCatalog, model.EffectiveSelectionKind);
        Assert.Null(model.SelectedCustomDriverSet);
        Assert.Null(model.ResolveEffectiveCustomDriverSet());
        Assert.False(model.HasCustomDriverIntegrityWarning);

        model.ConfigureCustomDriverSets(CreateAvailableCatalog("set-a"), "unknown-set");

        Assert.Equal(DriverPackSelectionKind.MicrosoftUpdateCatalog, model.EffectiveSelectionKind);
        Assert.Null(model.SelectedCustomDriverSet);
        Assert.False(model.HasCustomDriverIntegrityWarning);
    }

    [Fact]
    public void InvalidCatalogFallsBackAndRaisesIntegrityWarning()
    {
        using DriverPackSelectionViewModel model = CreatePhysicalLenovoModel();
        var changed = new List<string?>();
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        model.ConfigureCustomDriverSets(new CustomDriverSetCatalog(CustomDriverSetCatalogStatus.Invalid, []), "set-a");

        Assert.True(model.HasCustomDriverIntegrityWarning);
        Assert.Contains(nameof(DriverPackSelectionViewModel.HasCustomDriverIntegrityWarning), changed);
        Assert.Equal(DriverPackSelectionKind.MicrosoftUpdateCatalog, model.EffectiveSelectionKind);
        Assert.DoesNotContain(model.DriverPackOptions, option => option.Kind == DriverPackSelectionKind.CustomDriverSet);
        Assert.True(model.HasValidSelection());
    }

    [Fact]
    public void CustomSelectionRequiresASet()
    {
        using DriverPackSelectionViewModel model = CreatePhysicalLenovoModel();
        model.ConfigureCustomDriverSets(CreateAvailableCatalog("set-a", "set-b"), null);

        model.SelectedDriverPackOption = model.DriverPackOptions.Single(option => option.Kind == DriverPackSelectionKind.CustomDriverSet);

        Assert.True(model.IsCustomDriverSourceSelected);
        Assert.True(model.IsCustomDriverSetSelectionEnabled);
        Assert.Null(model.SelectedCustomDriverSet);
        Assert.False(model.HasValidSelection());

        model.SelectedCustomDriverSet = model.CustomDriverSetOptions[0];

        Assert.True(model.HasValidSelection());
        Assert.Equal("set-a", model.ResolveEffectiveCustomDriverSet()?.Id);

        model.SelectedDriverPackOption = model.DriverPackOptions.Single(option => option.Kind == DriverPackSelectionKind.MicrosoftUpdateCatalog);

        Assert.Null(model.ResolveEffectiveCustomDriverSet());
    }

    [Fact]
    public void SummaryShowsSetName()
    {
        using DriverPackSelectionViewModel model = CreatePhysicalLenovoModel();
        model.ConfigureCustomDriverSets(CreateAvailableCatalog("set-a", "set-b"), "set-a");

        Assert.Equal($"{model.Strings["DriverPack.CustomDriverSet"]} | Set set-a", model.SelectedDriverPackSelectionDisplay);
        Assert.Equal(model.Strings["DriverPack.CustomDriverSet"], model.DriverPackOptions.Single(option => option.Kind == DriverPackSelectionKind.CustomDriverSet).DisplayName);

        model.SelectedCustomDriverSet = model.CustomDriverSetOptions[1];

        Assert.Equal($"{model.Strings["DriverPack.CustomDriverSet"]} | Set set-b", model.SelectedDriverPackSelectionDisplay);
    }

    private static DriverPackSelectionViewModel CreatePhysicalLenovoModel()
    {
        var model = new DriverPackSelectionViewModel(
            new DriverPackSelectionService(NullLogger<DriverPackSelectionService>.Instance), new ResourceLocalizationService(LocalizationText.ResourceManager), "x64");
        model.UpdateSelectionContext(
            new HardwareProfile { Manufacturer = "Lenovo", Model = "ThinkPad T14 Gen 5", Product = "21ML" },
            new OperatingSystemCatalogItem { WindowsRelease = "11", ReleaseId = "25H2", Architecture = "x64" },
            "x64");
        model.ReplaceCatalog([CreateCatalogItem("x13", "25H2", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))]);
        return model;
    }

    private static CustomDriverSetCatalog CreateAvailableCatalog(params string[] ids)
    {
        return new CustomDriverSetCatalog(
            CustomDriverSetCatalogStatus.Available,
            ids.Select(id => new CustomDriverSetOption
            {
                Id = id,
                DisplayName = $"Set {id}",
                ContentHash = new string('a', 64),
                Length = 10,
                FileCount = 1,
                InfCount = 1,
                MediaRoot = @"E:\",
                Manifest = new()
            }).ToArray());
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
