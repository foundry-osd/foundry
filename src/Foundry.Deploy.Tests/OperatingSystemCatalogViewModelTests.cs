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
        Assert.True(viewModel.IsLicenseChannelSelectionEnabled);
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
        Assert.True(viewModel.IsLicenseChannelSelectionEnabled);
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
            Assert.EndsWith("(Most recent)", viewModel.MediaFilters[0].DisplayName);
            Assert.Contains("26200.9000", viewModel.MediaFilters[0].DisplayName);
            Assert.Contains("26200.8873", viewModel.MediaFilters[1].DisplayName);
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
        string build = "26200.8873")
    {
        return new OperatingSystemCatalogItem
        {
            SourceId = sourceId ?? $"{releaseId}-{licenseChannel}",
            WindowsRelease = "11",
            ReleaseId = releaseId,
            Architecture = "x64",
            LanguageCode = languageCode,
            Edition = "Pro",
            LicenseChannel = licenseChannel,
            Build = build,
            MediaDate = mediaDate ?? new DateOnly(2026, 7, 10),
            Url = $"https://example.test/windows-{sourceId ?? releaseId}-{licenseChannel}-{languageCode}.iso"
        };
    }
}
