// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Resources;

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

    public static TheoryData<string> SinglePageKeys => new()
    {
        "Action.Retry", "Action.StayHere",
        "Debug.Scenario.ApipaAddress", "Debug.Scenario.CaptivePortal", "Debug.Scenario.Checking", "Debug.Scenario.Dot1xFailed",
        "Debug.Scenario.EthernetNoInternet", "Debug.Scenario.EthernetReady", "Debug.Scenario.Live", "Debug.Scenario.NoAdapter",
        "Debug.Scenario.NoLink", "Debug.Scenario.NoWifiAdapter", "Debug.Scenario.ProvisionedWifiUnavailable", "Debug.Scenario.WaitingForAddress",
        "Debug.Scenario.WifiNetworks", "Debug.Scenario.WifiReady", "Debug.Scenario.WifiRuntimeMissing", "Debug.Scenarios",
        "Ethernet.ApipaAddress", "Ethernet.Dot1xAuthenticated", "Ethernet.Dot1xAuthenticating", "Ethernet.Dot1xFailed", "Ethernet.Dot1xLabel",
        "Ethernet.Dot1xProfileApplied", "Ethernet.Dot1xProvisioningFailed", "Ethernet.Dot1xReasonAuthenticationFailed",
        "Ethernet.Dot1xReasonProfileImportFailed", "Ethernet.Dot1xReasonProfileTemplateMissing", "Ethernet.Dot1xReasonSecretUnavailable",
        "Ethernet.Dot1xUnavailable", "Notice.CaptivePortalMessage", "Notice.CaptivePortalTitle",
        "Status.ApplyingProvisioningDescription", "Status.AutoContinueCancelled", "Status.AutoContinuePaused", "Status.BlockedCaptivePortalDescription",
        "Status.BlockedNoAdapterDescription", "Status.BlockedNoInternetDescription", "Status.BlockedNoIpAddressDescription", "Status.BlockedNoLinkDescription",
        "Status.BlockedTitle", "Status.CheckingDescription", "Status.CheckingTitle", "Status.ConnectingDescription", "Status.ConnectingTitle",
        "Wifi.ActionConnectTimedOut", "Wifi.ActionInvalidPassphrase", "Wifi.ActionNetworkNotVisible", "Wifi.ActionProvisionedSecretUnavailable",
        "Wifi.ConnectProvisionedAutomationName", "Wifi.ConnectSelectedAutomationFormat", "Wifi.DisconnectProvisionedAutomationName",
        "Wifi.DisconnectSelectedAutomationFormat", "Wifi.NetworkAutomationFormat", "Wifi.NetworkConnectedAutomationFormat",
        "Wifi.NetworksListAutomationName", "Wifi.PassphraseLabel", "Wifi.ProvisionedUnavailableConfiguration", "Wifi.ProvisionedUnavailablePassphrase",
        "Wifi.ProvisionedUnavailableSecret", "Wifi.ProvisionedUnavailableTemplate", "Wifi.ProvisionedUnavailableTitle", "Wifi.SecurityOwe",
        "Wifi.SelectNetworkHint", "Wifi.StatusNotInRange"
    };

    public static TheoryData<string> UnlockKeys => new()
    {
        "Ethernet.Dot1xReasonUnlockCancelled", "Unlock.Continue", "Unlock.Description", "Unlock.Heading", "Unlock.InvalidPassword",
        "Unlock.PasswordPlaceholder", "Unlock.Title", "Unlock.TogglePasswordVisibility", "Wifi.ActionProvisionedLocked",
        "Wifi.ProvisionedLocked", "Wifi.ProvisionedUnlockCancelled", "Wifi.StatusLocked"
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
    [MemberData(nameof(SinglePageKeys))]
    public void EveryCulture_ContainsSinglePageKey(string key)
    {
        ResourceManager resourceManager = new("Foundry.Connect.Strings.Resources", typeof(App).Assembly);

        foreach (string cultureName in CultureNames)
        {
            ResourceSet? resourceSet = resourceManager.GetResourceSet(CultureInfo.GetCultureInfo(cultureName), createIfNotExists: true, tryParents: false);
            Assert.False(string.IsNullOrWhiteSpace(resourceSet?.GetString(key)), $"{cultureName} {key}");
        }
    }

    [Theory]
    [MemberData(nameof(UnlockKeys))]
    public void EveryCulture_ContainsUnlockKey(string key)
    {
        ResourceManager resourceManager = new("Foundry.Connect.Strings.Resources", typeof(App).Assembly);

        foreach (string cultureName in CultureNames)
        {
            ResourceSet? resourceSet = resourceManager.GetResourceSet(CultureInfo.GetCultureInfo(cultureName), createIfNotExists: true, tryParents: false);
            Assert.False(string.IsNullOrWhiteSpace(resourceSet?.GetString(key)), $"{cultureName} {key}");
        }
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
