// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Resources;
using Foundry.Connect.Tests.Support;

namespace Foundry.Connect.Tests;

public sealed class LocalizationResourceTests
{
    private static readonly string[] CultureNames =
    [
        "ar-SA",
        "bg-BG",
        "cs-CZ",
        "da-DK",
        "de-DE",
        "el-GR",
        "en-GB",
        "en-US",
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
    ];

    public static TheoryData<string> SupportedCultures => new(CultureNames);

    // Texts of the page, its bindings and the unlock prompt: the parity tests keep the key sets equal, this keeps them filled.
    public static TheoryData<string> PageKeys => new()
    {
        "Action.Retry", "Action.StayHere", "Debug.Scenario.ApipaAddress", "Debug.Scenario.CaptivePortal", "Debug.Scenario.Checking",
        "Debug.Scenario.Dot1xAuthenticating", "Debug.Scenario.Dot1xFailed", "Debug.Scenario.EthernetNoInternet",
        "Debug.Scenario.EthernetReady", "Debug.Scenario.Live", "Debug.Scenario.NoAdapter", "Debug.Scenario.NoLink",
        "Debug.Scenario.NoWifiAdapter", "Debug.Scenario.ProvisionedWifiLocked", "Debug.Scenario.ProvisionedWifiUnavailable",
        "Debug.Scenario.WaitingForAddress", "Debug.Scenario.WifiConnecting", "Debug.Scenario.WifiNetworks", "Debug.Scenario.WifiOnly",
        "Debug.Scenario.WifiReady", "Debug.Scenario.WifiRuntimeMissing", "Debug.Scenarios", "Ethernet.ApipaAddress",
        "Ethernet.Dot1xAuthenticated", "Ethernet.Dot1xAuthenticating", "Ethernet.Dot1xFailed", "Ethernet.Dot1xLabel",
        "Ethernet.Dot1xProfileApplied", "Ethernet.Dot1xReasonAuthenticationFailed", "Ethernet.Dot1xReasonProfileImportFailed",
        "Ethernet.Dot1xReasonProfileTemplateMissing", "Ethernet.Dot1xReasonSecretUnavailable", "Ethernet.Dot1xReasonUnlockCancelled",
        "Ethernet.Dot1xUnavailable", "Ethernet.IpAssignmentLabel", "Ethernet.UnavailableOnDevice", "Status.ApplyingProvisioningDescription",
        "Status.AutoContinueCancelled", "Status.AutoContinuePaused", "Status.BlockedCaptivePortalDescription",
        "Status.BlockedCaptivePortalSignInDescription", "Status.BlockedCaptivePortalTitle", "Status.BlockedDot1xDescription", "Status.BlockedDot1xTitle",
        "Status.BlockedJoinWifiDescription", "Status.BlockedNoAdapterDescription", "Status.BlockedNoAdapterTitle",
        "Status.BlockedNoCableDescription", "Status.BlockedNoCableTitle", "Status.BlockedNoEthernetAdapterDescription",
        "Status.BlockedNoInternetDescription", "Status.BlockedNoIpAddressDescription", "Status.BlockedNoIpAddressTitle",
        "Status.BlockedNoLinkDescription", "Status.BlockedNoLinkTitle", "Status.BlockedTitle", "Status.CheckingTitle",
        "Status.ConnectingDescription", "Status.ConnectingTitle", "Status.DisconnectingTitle", "Status.Dot1xAuthenticatingDescription",
        "Unlock.Continue", "Unlock.Description", "Unlock.Heading", "Unlock.InvalidPassword", "Unlock.PasswordPlaceholder", "Unlock.Title",
        "Unlock.TogglePasswordVisibility", "Wifi.ActionConnectTimedOut", "Wifi.ActionInvalidPassphrase", "Wifi.ActionNetworkNotVisible",
        "Wifi.ConnectProvisionedAutomationName", "Wifi.ConnectSelectedAutomationFormat", "Wifi.DisconnectProvisionedAutomationName",
        "Wifi.DisconnectSelectedAutomationFormat", "Wifi.NetworkAutomationFormat", "Wifi.NetworkConnectedAutomationFormat",
        "Wifi.NetworksListAutomationName", "Wifi.PassphraseLabel", "Wifi.ProvisionedLocked", "Wifi.ProvisionedUnavailableConfiguration",
        "Wifi.ProvisionedUnavailablePassphrase", "Wifi.ProvisionedUnavailableSecret", "Wifi.ProvisionedUnavailableTemplate",
        "Wifi.ProvisionedUnlockCancelled", "Wifi.SecurityOwe", "Wifi.StatusLocked", "Wifi.StatusNotInRange", "Wifi.UnavailableOnDevice"
    };

    // Each blocked banner pairs a cause title with a description that says what to do; the description never repeats
    // the title, in any culture.
    public static TheoryData<string, string> TitleAndDescriptionPairs => new()
    {
        { "Status.BlockedNoLinkTitle", "Status.BlockedNoLinkDescription" },
        { "Status.BlockedNoLinkTitle", "Status.BlockedNoCableDescription" },
        { "Status.BlockedNoLinkTitle", "Status.BlockedJoinWifiDescription" },
        { "Status.BlockedDot1xTitle", "Status.BlockedDot1xDescription" }
    };

    [Theory]
    [MemberData(nameof(SupportedCultures))]
    public void ResourceSet_ContainsRequiredLabelsForAdkCulture(string cultureName)
    {
        ResourceManager resourceManager = new(
            "Foundry.Connect.Strings.Resources",
            typeof(App).Assembly);
        CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);

        ResourceSet? resourceSet = resourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);

        Assert.NotNull(resourceSet);
        Assert.Equal("Foundry Connect", resourceSet.GetString("App.Name"));
        Assert.False(string.IsNullOrWhiteSpace(resourceSet.GetString("Common.Cancel")));
        Assert.False(string.IsNullOrWhiteSpace(resourceSet.GetString("Common.Close")));
    }

    [Theory]
    [MemberData(nameof(PageKeys))]
    public void EveryCulture_ContainsPageKey(string key)
    {
        ResourceManager resourceManager = new("Foundry.Connect.Strings.Resources", typeof(App).Assembly);

        foreach (string cultureName in CultureNames)
        {
            ResourceSet? resourceSet = resourceManager.GetResourceSet(CultureInfo.GetCultureInfo(cultureName), createIfNotExists: true, tryParents: false);
            Assert.False(string.IsNullOrWhiteSpace(resourceSet?.GetString(key)), $"{cultureName} {key}");
        }
    }

    [Theory]
    [MemberData(nameof(TitleAndDescriptionPairs))]
    public void BlockedDescriptions_DoNotRepeatTheTitle(string titleKey, string descriptionKey)
    {
        ResourceManager resourceManager = new("Foundry.Connect.Strings.Resources", typeof(App).Assembly);

        Assert.All(CultureNames, cultureName =>
        {
            ResourceSet resourceSet = resourceManager.GetResourceSet(CultureInfo.GetCultureInfo(cultureName), createIfNotExists: true, tryParents: false)!;
            Assert.DoesNotContain(resourceSet.GetString(titleKey)!, resourceSet.GetString(descriptionKey), StringComparison.Ordinal);
        });
    }

    [Theory]
    [MemberData(nameof(SupportedCultures))]
    public void WiredTexts_DoNotMentionWifi(string cultureName)
    {
        ResourceManager resourceManager = new("Foundry.Connect.Strings.Resources", typeof(App).Assembly);
        ResourceSet resourceSet = resourceManager.GetResourceSet(CultureInfo.GetCultureInfo(cultureName), createIfNotExists: true, tryParents: false)!;

        foreach (string key in new[]
        {
            "Ethernet.Dot1xReasonUnlockCancelled", "Ethernet.UnavailableOnDevice", "Status.BlockedNoEthernetAdapterDescription", "Status.BlockedNoCableDescription",
            "Status.BlockedNoAdapterTitle", "Status.BlockedNoCableTitle", "Status.BlockedNoLinkTitle", "Status.BlockedDot1xTitle", "Status.BlockedNoIpAddressTitle",
            "Status.BlockedCaptivePortalSignInDescription", "Status.BlockedCaptivePortalTitle", "Status.BlockedTitle"
        })
        {
            Assert.DoesNotContain("Wi-Fi", resourceSet.GetString(key), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("Ethernet.Dot1xReasonUnlockCancelled")]
    [InlineData("Wifi.ProvisionedUnavailableSecret")]
    [InlineData("Wifi.ProvisionedUnlockCancelled")]
    [InlineData("Wifi.UnavailableOnDevice")]
    [InlineData("Ethernet.UnavailableOnDevice")]
    public void SectionTexts_CarryNoAlternativePathOrDriverAdvice(string key)
    {
        string? value = ConnectResources.NeutralValue(key);

        Assert.NotNull(value);
        Assert.DoesNotContain("still work", value, StringComparison.Ordinal);
        Assert.DoesNotContain("You can also", value, StringComparison.Ordinal);
        Assert.DoesNotContain("driver", value, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(SupportedCultures))]
    public void MenuHeaders_HaveAccessKeys(string cultureName)
    {
        ResourceManager resourceManager = new("Foundry.Connect.Strings.Resources", typeof(App).Assembly);
        ResourceSet resourceSet = resourceManager.GetResourceSet(CultureInfo.GetCultureInfo(cultureName), createIfNotExists: true, tryParents: false)!;

        foreach (string key in new[] { "Menu.About", "Menu.Debug", "Menu.Keyboard", "Menu.KeyboardFormat", "Menu.Language", "Menu.Theme", "Menu.Tools" })
        {
            Assert.Contains("_", resourceSet.GetString(key), StringComparison.Ordinal);
        }
    }
}
