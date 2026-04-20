using Foundry.Deploy.Models;
using Foundry.Deploy.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class OperatingSystemCatalogViewModelTests
{
    [Fact]
    public void ApplyExpertLocalization_UsesCanonicalLanguageCodesForFiltersAndSelection()
    {
        var viewModel = new OperatingSystemCatalogViewModel(NullLogger.Instance, "x64");
        viewModel.ApplyCatalog(
        [
            CreateOperatingSystem("fr-fr"),
            CreateOperatingSystem("EN_us")
        ]);

        Assert.Contains("en-US", viewModel.LanguageFilters);
        Assert.Contains("fr-FR", viewModel.LanguageFilters);

        viewModel.ApplyExpertLocalization([" fr_FR "], "FR-fr", forceSingleVisibleLanguageSelection: true);

        Assert.Equal(["fr-FR"], viewModel.LanguageFilters);
        Assert.Equal("fr-FR", viewModel.SelectedLanguageCode);
        Assert.False(viewModel.IsLanguageSelectionEnabled);
    }

    private static OperatingSystemCatalogItem CreateOperatingSystem(string languageCode)
    {
        return new OperatingSystemCatalogItem
        {
            WindowsRelease = "11",
            ReleaseId = "25H2",
            Architecture = "x64",
            LanguageCode = languageCode,
            Edition = "Pro",
            LicenseChannel = "RET",
            Build = "26200",
            Url = $"https://example.test/windows-{languageCode}.iso"
        };
    }
}
