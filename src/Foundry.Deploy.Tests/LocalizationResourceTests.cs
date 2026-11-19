// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Resources;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using Foundry.Deploy.Services.Localization;

namespace Foundry.Deploy.Tests;

public sealed class LocalizationResourceTests
{
    [Theory]
    [InlineData("en-US")]
    [MemberData(nameof(SatelliteCultures))]
    public void ConditionalWorkflowResources_ExistInEachCulture_WithMatchingPlaceholders(string cultureName)
    {
        string stringsRoot = Path.Combine(FindRepositoryRoot(), "src", "Foundry.Deploy", "Strings");
        XDocument reference = XDocument.Load(Path.Combine(stringsRoot, "en-US", "Resources.resx"));
        XDocument localized = XDocument.Load(Path.Combine(stringsRoot, cultureName, "Resources.resx"));
        string[] requiredKeys =
        [
            "Step.CheckWindowsImage", "Step.ConfigureWindowsBoot", "Step.ConfigureAiPolicies",
            "Step.StageDriverInstaller", "Step.ExtractFirmwareUpdate", "Step.RegisterAutopilotDevice",
            "Step.PrepareAutopilotAssistant", "Step.CopyAutopilotProfile",
            "StepMessage.CheckingAvailableSpace", "StepResult.WindowsImageChecked",
            "StepResult.WindowsBootConfigured", "StepResult.ExternalStorageReady",
            "StepResult.DriverInstallerStaged", "StepResult.FirmwareUpdateExtracted",
            "StepResult.FirmwareUpdateStaged", "StepResult.FirmwareUpdateResolvedFromCache",
            "StepResult.WindowsImageNotApplied", "StepResult.NoDeferredDriverInstallerRequired",
            "StepMessage.CopyingDriverPackage", "StepMessage.ExtractingFirmwareUpdate",
            "StepMessage.StagingFirmwareUpdate", "StepResult.NoFirmwareUpdatePayload",
            "StepResult.SelectedFirmwarePayloadUnavailable", "StepResult.SelectedDriverPayloadUnavailable",
            "StepResult.SimulationFormat", "StepResult.CatalogDriverInfMissingFormat", "StepResult.NoFirmwareCabFiles",
            "Preflight.DownloadVerificationFailed", "Preflight.LegacyBiosBoot",
            "DomainJoin.CheckingCredentials", "DomainJoin.CredentialsRejected", "DomainJoin.AccountLocked",
            "DomainJoin.PasswordExpired", "DomainJoin.AccountDisabled", "DomainJoin.DomainUnreachable", "DomainJoin.CheckInconclusive",
            "DomainJoin.ZeroTouchCredentialsRejected", "DomainJoin.ZeroTouchAccountLocked", "DomainJoin.ZeroTouchPasswordExpired",
            "DomainJoin.ZeroTouchAccountDisabled", "DomainJoin.ZeroTouchDomainUnreachable", "DomainJoin.ZeroTouchCheckInconclusive",
            "DomainJoin.CredentialsVerified",
            "Step.StageCustomDrivers", "StepResult.CustomDriversStaged", "StepResult.CustomDriversUnavailable",
            "CatalogStartup.Title", "CatalogStartup.Message"
        ];
        foreach (string key in requiredKeys)
        {
            string translated = GetResourceValue(localized, key);
            Assert.False(string.IsNullOrWhiteSpace(translated));
            string[] expected = Regex.Matches(GetResourceValue(reference, key), @"\{\d+(?:[^}]*)\}").Select(match => match.Value).Order().ToArray();
            string[] actual = Regex.Matches(translated, @"\{\d+(?:[^}]*)\}").Select(match => match.Value).Order().ToArray();
            Assert.Equal(expected, actual);
        }
        Assert.Equal(localized.Root!.Elements("data").Count(), localized.Root.Elements("data").Select(element => (string?)element.Attribute("name")).Distinct().Count());
    }

    private static readonly string[] DeploymentAccessResourceKeys =
    [
        "Common.Cancel",
        "DeploymentAccess.Title",
        "DeploymentAccess.Heading",
        "DeploymentAccess.Description",
        "DeploymentAccess.PasswordPlaceholder",
        "DeploymentAccess.Continue",
        "DeploymentAccess.TogglePasswordVisibility",
        "DeploymentAccess.InvalidPassword"
    ];

    public static TheoryData<string> SatelliteCultures => new()
    {
        "ar-SA",
        "bg-BG",
        "cs-CZ",
        "da-DK",
        "de-DE",
        "el-GR",
        "en-GB",
        "es-ES",
        "es-MX",
        "et-EE",
        "fi-FI",
        "fr-CA",
        "fr-FR",
        "he-IL",
        "hr-HR",
        "hu-HU",
        "it-IT",
        "ja-JP",
        "ko-KR",
        "lt-LT",
        "lv-LV",
        "nb-NO",
        "nl-NL",
        "pl-PL",
        "pt-BR",
        "pt-PT",
        "ro-RO",
        "ru-RU",
        "sk-SK",
        "sl-SI",
        "sr-Latn-RS",
        "sv-SE",
        "th-TH",
        "tr-TR",
        "uk-UA",
        "zh-CN",
        "zh-TW"
    };

    [Theory]
    [MemberData(nameof(SatelliteCultures))]
    public void SatelliteResourceSet_IsAvailableForSupportedCulture(string cultureName)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);

        ResourceSet? resourceSet = LocalizationText.ResourceManager.GetResourceSet(
            culture,
            createIfNotExists: true,
            tryParents: false);

        Assert.NotNull(resourceSet);
        Assert.Equal("Foundry Deploy", resourceSet.GetString("App.Name"));
        foreach (string key in DeploymentAccessResourceKeys)
        {
            Assert.False(string.IsNullOrWhiteSpace(resourceSet.GetString(key)), $"Resource '{key}' is missing for '{cultureName}'.");
        }

        Assert.Contains("{0}", resourceSet.GetString("Error.FailedStepFormat"));
    }

    [Theory]
    [InlineData("en-US")]
    [MemberData(nameof(SatelliteCultures))]
    public void MenuHeaders_HaveAccessKeys(string cultureName)
    {
        ResourceSet resourceSet = LocalizationText.ResourceManager.GetResourceSet(
            CultureInfo.GetCultureInfo(cultureName), createIfNotExists: true, tryParents: false)!;

        foreach (string key in TopLevelMenuHeaderKeys.Append("Menu.Network"))
        {
            Assert.Contains("_", resourceSet.GetString(key), StringComparison.Ordinal);
        }

        foreach (string key in new[] { "Network.State.Disconnected", "Network.State.Ethernet", "Network.State.Limited", "Network.State.Wifi" })
        {
            Assert.False(string.IsNullOrWhiteSpace(resourceSet.GetString(key)), $"Resource '{key}' is missing for '{cultureName}'.");
        }
    }

    // The Keyboard header has no access key in Deploy, so only these headers can collide with the Network item.
    [Theory]
    [InlineData("en-US")]
    [MemberData(nameof(SatelliteCultures))]
    public void NetworkMenuAccessKey_DiffersFromTheOtherTopLevelHeaders(string cultureName)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
        ResourceSet resourceSet = LocalizationText.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!;

        string AccessKey(string key)
        {
            string value = resourceSet.GetString(key)!;
            return value[(value.IndexOf('_', StringComparison.Ordinal) + 1)..][..1].ToUpper(culture);
        }

        string network = AccessKey("Menu.Network");
        foreach (string key in TopLevelMenuHeaderKeys)
        {
            Assert.NotEqual(AccessKey(key), network);
        }
    }

    // Dialog buttons have no access key; the caption is the Network menu header's.
    [Theory]
    [InlineData("en-US")]
    [MemberData(nameof(SatelliteCultures))]
    public void NetworkDialogButton_IsTheMenuHeaderWithoutItsAccessKey(string cultureName)
    {
        ResourceSet resourceSet = LocalizationText.ResourceManager.GetResourceSet(
            CultureInfo.GetCultureInfo(cultureName), createIfNotExists: true, tryParents: false)!;

        string header = Regex.Replace(resourceSet.GetString("Menu.Network")!, @"\(_.\)$", string.Empty).Replace("_", string.Empty, StringComparison.Ordinal);

        Assert.Equal(header, resourceSet.GetString("Common.Network"));
    }

    private static readonly string[] TopLevelMenuHeaderKeys = ["Menu.About", "Menu.Debug", "Menu.Language", "Menu.Theme", "Menu.Tools"];

    [Theory]
    [InlineData("en-US")]
    [MemberData(nameof(SatelliteCultures))]
    public void ProvisioningModeTitles_MatchFoundryPageTitles(string cultureName)
    {
        string repositoryRoot = FindRepositoryRoot();
        string foundryResourcePath = Path.Combine(repositoryRoot, "src", "Foundry", "Strings", cultureName, "Resources.resw");
        string deployResourcePath = Path.Combine(repositoryRoot, "src", "Foundry.Deploy", "Strings", cultureName, "Resources.resx");

        XDocument foundryResources = XDocument.Load(foundryResourcePath);
        XDocument deployResources = XDocument.Load(deployResourcePath);

        Assert.Equal(
            GetResourceValue(foundryResources, "AutopilotJsonProfilePageHeader.Title"),
            GetResourceValue(deployResources, "Preparation.AutopilotModeJsonProfile"));
        Assert.Equal(
            GetResourceValue(foundryResources, "AutopilotZeroTouchPageHeader.Title"),
            GetResourceValue(deployResources, "Preparation.AutopilotModeHardwareHashUpload"));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "Foundry.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string GetResourceValue(XDocument document, string key) =>
        document.Root?
            .Elements("data")
            .Single(element => string.Equals((string?)element.Attribute("name"), key, StringComparison.Ordinal))
            .Element("value")?
            .Value
        ?? throw new InvalidDataException($"Resource '{key}' is missing.");
}
