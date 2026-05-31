using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class OperatingSystemCatalogViewModelTests
{
    [Fact]
    public void ApplyOperatingSystemSelection_UsesCanonicalLanguageCodesForFiltersAndSelection()
    {
        var viewModel = new OperatingSystemCatalogViewModel(NullLogger.Instance, "x64");
        viewModel.ApplyCatalog(
        [
            CreateOperatingSystem("fr-fr"),
            CreateOperatingSystem("EN_us")
        ]);

        Assert.Contains("en-US", viewModel.LanguageFilters);
        Assert.Contains("fr-FR", viewModel.LanguageFilters);

        viewModel.ApplyOperatingSystemSelection(new DeployOperatingSystemSelectionSettings
        {
            AllowedLanguageCodes = [" fr_FR "],
            DefaultLanguageCode = "FR-fr"
        });

        Assert.Equal(["fr-FR"], viewModel.LanguageFilters);
        Assert.Equal("fr-FR", viewModel.SelectedLanguageCode);
        Assert.False(viewModel.IsLanguageSelectionEnabled);
    }

    [Fact]
    public void ApplyOperatingSystemSelection_RestrictsReleaseLicenseAndEditionAndDisablesSingleConfiguredOptions()
    {
        var viewModel = new OperatingSystemCatalogViewModel(NullLogger.Instance, "x64");
        viewModel.ApplyCatalog(
        [
            CreateOperatingSystem("en-US", releaseId: "25H2", licenseChannel: "RET"),
            CreateOperatingSystem("en-US", releaseId: "24H2", licenseChannel: "VOL")
        ]);

        viewModel.ApplyOperatingSystemSelection(new DeployOperatingSystemSelectionSettings
        {
            AllowedReleaseIds = ["24h2"],
            DefaultReleaseId = "24H2",
            AllowedLicenseChannels = ["volume"],
            DefaultLicenseChannel = "vol",
            AllowedEditions = ["Enterprise"],
            DefaultEdition = "Enterprise"
        });

        Assert.Equal(["24H2"], viewModel.ReleaseIdFilters);
        Assert.False(viewModel.IsReleaseIdSelectionEnabled);
        Assert.Equal(["VOL"], viewModel.LicenseChannelFilters);
        Assert.False(viewModel.IsLicenseChannelSelectionEnabled);
        Assert.Equal(["Enterprise"], viewModel.EditionFilters);
        Assert.False(viewModel.IsEditionSelectionEnabled);
        Assert.Equal("Enterprise", viewModel.SelectedOperatingSystem?.Edition);
    }

    [Fact]
    public void ApplyOperatingSystemSelection_WhenAllowedValuesAreUnavailable_FallsBackToCatalogScope()
    {
        var viewModel = new OperatingSystemCatalogViewModel(NullLogger.Instance, "x64");
        viewModel.ApplyCatalog(
        [
            CreateOperatingSystem("en-US", releaseId: "25H2", licenseChannel: "RET")
        ]);

        viewModel.ApplyOperatingSystemSelection(new DeployOperatingSystemSelectionSettings
        {
            AllowedReleaseIds = ["23H2"],
            DefaultReleaseId = "23H2",
            AllowedLicenseChannels = ["VOL"],
            DefaultLicenseChannel = "VOL",
            AllowedEditions = ["Datacenter"],
            DefaultEdition = "Datacenter"
        });

        Assert.Equal(["25H2"], viewModel.ReleaseIdFilters);
        Assert.True(viewModel.IsReleaseIdSelectionEnabled);
        Assert.Equal(["RET"], viewModel.LicenseChannelFilters);
        Assert.True(viewModel.IsLicenseChannelSelectionEnabled);
        Assert.Contains("Pro", viewModel.EditionFilters);
        Assert.True(viewModel.IsEditionSelectionEnabled);
    }

    private static OperatingSystemCatalogItem CreateOperatingSystem(
        string languageCode,
        string releaseId = "25H2",
        string licenseChannel = "RET")
    {
        return new OperatingSystemCatalogItem
        {
            WindowsRelease = "11",
            ReleaseId = releaseId,
            Architecture = "x64",
            LanguageCode = languageCode,
            Edition = "Pro",
            LicenseChannel = licenseChannel,
            Build = "26200",
            Url = $"https://example.test/windows-{releaseId}-{licenseChannel}-{languageCode}.iso"
        };
    }
}
