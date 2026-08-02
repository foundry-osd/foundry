// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;

namespace Foundry.Deploy.Tests;

public sealed class OperatingSystemCatalogViewModelTests
{
    [Theory]
    [InlineData("x64")]
    [InlineData("arm64")]
    public void ApplyCatalog_ForRetailMedia_DoesNotOfferEnterprise(string architecture)
    {
        var viewModel = new OperatingSystemCatalogViewModel(NullLogger.Instance, architecture);

        viewModel.ApplyCatalog(
        [
            CreateOperatingSystem("en-US", licenseChannel: "RET", architecture: architecture)
        ]);

        Assert.Contains("Pro", viewModel.EditionFilters);
        Assert.DoesNotContain("Enterprise", viewModel.EditionFilters);
        Assert.DoesNotContain("Enterprise N", viewModel.EditionFilters);
    }

    [Fact]
    public void ApplyCatalog_ForVolumeMedia_OffersEnterprise()
    {
        var viewModel = new OperatingSystemCatalogViewModel(NullLogger.Instance, "x64");

        viewModel.ApplyCatalog(
        [
            CreateOperatingSystem("en-US", licenseChannel: "VOL")
        ]);

        Assert.Contains("Enterprise", viewModel.EditionFilters);
        Assert.Contains("Enterprise N", viewModel.EditionFilters);
    }

    [Theory]
    [InlineData("Home", "RET")]
    [InlineData("Home N", "RET")]
    [InlineData("Home Single Language", "RET")]
    [InlineData("Enterprise", "VOL")]
    [InlineData("Enterprise N", "VOL")]
    public void SelectingChannelSpecificEdition_ForcesItsLicenseChannel(string edition, string expectedChannel)
    {
        var viewModel = new OperatingSystemCatalogViewModel(NullLogger.Instance, "x64");
        viewModel.ApplyCatalog(
        [
            CreateOperatingSystem("en-US", licenseChannel: "RET", sourceId: "media"),
            CreateOperatingSystem("en-US", licenseChannel: "VOL", sourceId: "media")
        ]);

        viewModel.SelectedEdition = edition;

        Assert.Equal([expectedChannel], viewModel.LicenseChannelFilters);
        Assert.Equal(expectedChannel, viewModel.SelectedLicenseChannel);
        Assert.False(viewModel.IsLicenseChannelSelectionEnabled);
        Assert.Equal(edition, viewModel.SelectedOperatingSystem?.Edition);
    }

    [Theory]
    [InlineData("Pro")]
    [InlineData("Pro N")]
    [InlineData("Education")]
    [InlineData("Education N")]
    public void SelectingDualChannelEdition_OffersRetailAndVolume(string edition)
    {
        var viewModel = new OperatingSystemCatalogViewModel(NullLogger.Instance, "x64");
        viewModel.ApplyCatalog(
        [
            CreateOperatingSystem("en-US", licenseChannel: "RET", sourceId: "media"),
            CreateOperatingSystem("en-US", licenseChannel: "VOL", sourceId: "media")
        ]);

        viewModel.SelectedEdition = edition;

        Assert.Equal(["RET", "VOL"], viewModel.LicenseChannelFilters);
        Assert.True(viewModel.IsLicenseChannelSelectionEnabled);
    }

    [Fact]
    public void SelectingHomeChina_SelectsOnlyCountrySpecificMedia()
    {
        var viewModel = new OperatingSystemCatalogViewModel(NullLogger.Instance, "x64");
        viewModel.ApplyCatalog(
        [
            CreateOperatingSystem("zh-CN", licenseChannel: "RET", catalogEdition: "UltimateN", sourceSuffix: "consumer"),
            CreateOperatingSystem("zh-CN", licenseChannel: "RET", catalogEdition: "CoreCountrySpecific", sourceSuffix: "china")
        ]);

        Assert.Contains("Home China", viewModel.EditionFilters);

        viewModel.SelectedEdition = "Home China";

        Assert.Contains("china", viewModel.SelectedOperatingSystem?.Url, StringComparison.Ordinal);
        Assert.Equal("Home China", viewModel.SelectedOperatingSystem?.Edition);
    }

    [Fact]
    public void ApplyCatalog_ForArm64_OffersOnlyAvailableArm64Editions()
    {
        var viewModel = new OperatingSystemCatalogViewModel(NullLogger.Instance, "arm64");
        viewModel.ApplyCatalog(
        [
            CreateOperatingSystem("en-US", licenseChannel: "RET", architecture: "arm64", sourceId: "media"),
            CreateOperatingSystem("en-US", licenseChannel: "VOL", architecture: "arm64", sourceId: "media")
        ]);

        Assert.Equal(
            ["Education", "Education N", "Enterprise", "Enterprise N", "Home", "Home N", "Home Single Language", "Pro", "Pro N"],
            viewModel.EditionFilters);

        viewModel.SelectedEdition = "Enterprise";

        Assert.Equal(["VOL"], viewModel.LicenseChannelFilters);
        Assert.Equal("VOL", viewModel.SelectedLicenseChannel);
    }

    [Fact]
    public void ApplyOperatingSystemSelection_WithVolumeOnlyPolicy_DoesNotOfferRetailOnlyEditions()
    {
        var viewModel = new OperatingSystemCatalogViewModel(NullLogger.Instance, "x64");
        viewModel.ApplyCatalog(
        [
            CreateOperatingSystem("en-US", licenseChannel: "RET", sourceId: "media"),
            CreateOperatingSystem("en-US", licenseChannel: "VOL", sourceId: "media")
        ]);

        viewModel.ApplyOperatingSystemSelection(new DeployOperatingSystemSelectionSettings
        {
            IsEnabled = true,
            AllowedLicenseChannels = ["VOL"]
        });

        Assert.DoesNotContain("Home", viewModel.EditionFilters);
        Assert.DoesNotContain("Home N", viewModel.EditionFilters);
        Assert.DoesNotContain("Home Single Language", viewModel.EditionFilters);
        Assert.Contains("Enterprise", viewModel.EditionFilters);
        Assert.Equal(["VOL"], viewModel.LicenseChannelFilters);
    }

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
            IsEnabled = true,
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
            IsEnabled = true,
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
            IsEnabled = true,
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
        Assert.False(viewModel.IsLicenseChannelSelectionEnabled);
        Assert.Contains("Pro", viewModel.EditionFilters);
        Assert.True(viewModel.IsEditionSelectionEnabled);
    }

    [Fact]
    public void ApplyOperatingSystemSelection_WhenDisabled_IgnoresSavedPolicy()
    {
        var viewModel = new OperatingSystemCatalogViewModel(NullLogger.Instance, "x64");
        viewModel.ApplyCatalog(
        [
            CreateOperatingSystem("en-US", releaseId: "25H2", licenseChannel: "RET"),
            CreateOperatingSystem("fr-FR", releaseId: "24H2", licenseChannel: "VOL")
        ]);

        viewModel.ApplyOperatingSystemSelection(new DeployOperatingSystemSelectionSettings
        {
            IsEnabled = false,
            AllowedLanguageCodes = ["fr-FR"],
            DefaultLanguageCode = "fr-FR",
            AllowedReleaseIds = ["24H2"],
            DefaultReleaseId = "24H2",
            AllowedLicenseChannels = ["VOL"],
            DefaultLicenseChannel = "VOL",
            AllowedEditions = ["Enterprise"],
            DefaultEdition = "Enterprise"
        });

        Assert.Equal(["24H2", "25H2"], viewModel.ReleaseIdFilters);
        Assert.True(viewModel.IsReleaseIdSelectionEnabled);
        Assert.Equal(["en-US"], viewModel.LanguageFilters);
        Assert.True(viewModel.IsLanguageSelectionEnabled);
        Assert.Equal(["RET"], viewModel.LicenseChannelFilters);
        Assert.False(viewModel.IsLicenseChannelSelectionEnabled);
        Assert.Contains("Pro", viewModel.EditionFilters);
        Assert.True(viewModel.IsEditionSelectionEnabled);
    }

    [Fact]
    public void ApplyOperatingSystemSelection_WhenMediaOffsetExceedsHistory_ClampsToOldestAvailableMedia()
    {
        var viewModel = new OperatingSystemCatalogViewModel(NullLogger.Instance, "x64");
        viewModel.ApplyCatalog(
        [
            CreateOperatingSystem("en-US", sourceId: "july", mediaDate: new DateOnly(2026, 7, 10), build: "26200.8873"),
            CreateOperatingSystem("en-US", sourceId: "june", mediaDate: new DateOnly(2026, 6, 6), build: "26200.8653"),
            CreateOperatingSystem("en-US", sourceId: "may", mediaDate: new DateOnly(2026, 5, 7), build: "26200.8457")
        ]);

        viewModel.ApplyOperatingSystemSelection(new DeployOperatingSystemSelectionSettings
        {
            IsEnabled = true,
            DefaultReleaseId = "25H2",
            DefaultMediaOffset = 11
        });

        Assert.Equal(3, viewModel.MediaFilters.Count);
        Assert.Equal("may", viewModel.SelectedMediaSourceId);
        Assert.Equal(new DateOnly(2026, 5, 7), viewModel.SelectedOperatingSystem?.MediaDate);
    }

    [Theory]
    [InlineData(0, "july")]
    [InlineData(1, "june")]
    public void ApplyOperatingSystemSelection_SelectsRequestedAvailableMediaOffset(int offset, string expectedSourceId)
    {
        var viewModel = new OperatingSystemCatalogViewModel(NullLogger.Instance, "x64");
        viewModel.ApplyOperatingSystemSelection(new DeployOperatingSystemSelectionSettings
        {
            IsEnabled = true,
            DefaultReleaseId = "25H2",
            DefaultMediaOffset = offset
        });
        viewModel.ApplyCatalog(
        [
            CreateOperatingSystem("en-US", sourceId: "july", mediaDate: new DateOnly(2026, 7, 10), build: "26200.8873"),
            CreateOperatingSystem("en-US", sourceId: "june", mediaDate: new DateOnly(2026, 6, 6), build: "26200.8653")
        ]);

        Assert.Equal(expectedSourceId, viewModel.SelectedMediaSourceId);
    }

    [Fact]
    public void ApplyCatalog_WhenMonthContainsMultipleBuilds_IncludesBuildInMediaLabels()
    {
        CultureInfo originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var viewModel = new OperatingSystemCatalogViewModel(NullLogger.Instance, "x64");

            viewModel.ApplyCatalog(
            [
                CreateOperatingSystem("en-US", sourceId: "july-new", mediaDate: new DateOnly(2026, 7, 20), build: "26200.9000"),
                CreateOperatingSystem("en-US", sourceId: "july-old", mediaDate: new DateOnly(2026, 7, 10), build: "26200.8873")
            ]);

            Assert.StartsWith("July 2026", viewModel.MediaFilters[0].DisplayName);
            Assert.EndsWith("(Latest)", viewModel.MediaFilters[0].DisplayName);
            Assert.Contains("26200.9000", viewModel.MediaFilters[0].DisplayName);
            Assert.Contains("26200.8873", viewModel.MediaFilters[1].DisplayName);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void ApplyCatalog_WithLowercaseLocalizedMonth_CapitalizesMediaLabel()
    {
        CultureInfo originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            var viewModel = new OperatingSystemCatalogViewModel(NullLogger.Instance, "x64");

            viewModel.ApplyCatalog(
            [
                CreateOperatingSystem("fr-FR", sourceId: "july", mediaDate: new DateOnly(2026, 7, 10))
            ]);

            Assert.StartsWith("Juillet 2026", viewModel.MediaFilters[0].DisplayName);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    private static OperatingSystemCatalogItem CreateOperatingSystem(
        string languageCode,
        string releaseId = "25H2",
        string licenseChannel = "RET",
        string? sourceId = null,
        DateOnly? mediaDate = null,
        string build = "26200.8873",
        string architecture = "x64",
        string catalogEdition = "Pro",
        string? sourceSuffix = null)
    {
        return new OperatingSystemCatalogItem
        {
            SourceId = sourceId ?? $"{releaseId}-{licenseChannel}",
            ClientType = licenseChannel.Equals("VOL", StringComparison.OrdinalIgnoreCase)
                ? "CLIENTBUSINESS"
                : "CLIENTCONSUMER",
            WindowsRelease = "11",
            ReleaseId = releaseId,
            Architecture = architecture,
            LanguageCode = languageCode,
            Edition = catalogEdition,
            LicenseChannel = licenseChannel,
            Build = build,
            MediaDate = mediaDate ?? new DateOnly(2026, 7, 10),
            Url = $"https://example.test/windows-{sourceId ?? releaseId}-{licenseChannel}-{languageCode}{sourceSuffix}.iso"
        };
    }
}
