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

    public static TheoryData<string> SinglePageKeys => new()
    {
        "Action.Retry", "Action.StayHere",
        "Debug.Scenario.ApipaAddress", "Debug.Scenario.CaptivePortal", "Debug.Scenario.Checking", "Debug.Scenario.Dot1xFailed",
        "Debug.Scenario.EthernetNoInternet", "Debug.Scenario.EthernetReady", "Debug.Scenario.Live", "Debug.Scenario.NoAdapter",
        "Debug.Scenario.NoLink", "Debug.Scenario.NoWifiAdapter", "Debug.Scenario.ProvisionedWifiUnavailable", "Debug.Scenario.WaitingForAddress",
        "Debug.Scenario.WifiNetworks", "Debug.Scenario.WifiReady", "Debug.Scenario.WifiRuntimeMissing", "Debug.Scenarios",
        "Ethernet.ApipaAddress", "Ethernet.Dot1xAuthenticated", "Ethernet.Dot1xAuthenticating", "Ethernet.Dot1xFailed", "Ethernet.Dot1xLabel",
        "Ethernet.Dot1xProfileApplied", "Ethernet.Dot1xReasonAuthenticationFailed",
        "Ethernet.Dot1xReasonProfileImportFailed", "Ethernet.Dot1xReasonProfileTemplateMissing", "Ethernet.Dot1xReasonSecretUnavailable",
        "Ethernet.Dot1xUnavailable",
        "Status.ApplyingProvisioningDescription", "Status.AutoContinueCancelled", "Status.AutoContinuePaused", "Status.BlockedCaptivePortalDescription",
        "Status.BlockedNoAdapterDescription", "Status.BlockedNoInternetDescription", "Status.BlockedNoIpAddressDescription", "Status.BlockedNoLinkDescription",
        "Status.BlockedTitle", "Status.CheckingTitle", "Status.ConnectingDescription", "Status.ConnectingTitle",
        "Wifi.ActionConnectTimedOut", "Wifi.ActionInvalidPassphrase", "Wifi.ActionNetworkNotVisible",
        "Wifi.ConnectProvisionedAutomationName", "Wifi.ConnectSelectedAutomationFormat", "Wifi.DisconnectProvisionedAutomationName",
        "Wifi.DisconnectSelectedAutomationFormat", "Wifi.NetworkAutomationFormat", "Wifi.NetworkConnectedAutomationFormat",
        "Wifi.NetworksListAutomationName", "Wifi.PassphraseLabel", "Wifi.ProvisionedUnavailableConfiguration", "Wifi.ProvisionedUnavailablePassphrase",
        "Wifi.ProvisionedUnavailableSecret", "Wifi.ProvisionedUnavailableTemplate", "Wifi.SecurityOwe",
        "Wifi.StatusNotInRange"
    };

    public static TheoryData<string> UnlockKeys => new()
    {
        "Ethernet.Dot1xReasonUnlockCancelled", "Unlock.Continue", "Unlock.Description", "Unlock.Heading", "Unlock.InvalidPassword",
        "Unlock.PasswordPlaceholder", "Unlock.Title", "Unlock.TogglePasswordVisibility",
        "Wifi.ProvisionedLocked", "Wifi.ProvisionedUnlockCancelled", "Wifi.StatusLocked"
    };

    public static TheoryData<string> LayoutKeys => new()
    {
        "Debug.Scenario.Dot1xAuthenticating", "Debug.Scenario.ProvisionedWifiLocked", "Debug.Scenario.WifiConnecting", "Debug.Scenario.WifiOnly",
        "Ethernet.UnavailableOnDevice", "Status.BlockedDot1xDescription", "Status.BlockedJoinWifiDescription", "Status.BlockedNoCableDescription",
        "Status.BlockedNoEthernetAdapterDescription", "Status.DisconnectingTitle", "Status.Dot1xAuthenticatingDescription", "Wifi.UnavailableOnDevice"
    };

    public static TheoryData<string> BannerTitleAndLabelKeys => new()
    {
        "Ethernet.IpAssignmentLabel", "Status.BlockedCaptivePortalTitle", "Status.BlockedDot1xTitle", "Status.BlockedNoAdapterTitle",
        "Status.BlockedNoCableTitle", "Status.BlockedNoIpAddressTitle", "Status.BlockedNoLinkTitle"
    };

    // Each blocked banner pairs a cause title with a description that says what to do; the description no longer
    // repeats the cause, so no culture keeps the old first sentence in front of it.
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
    [MemberData(nameof(LayoutKeys))]
    public void EveryCulture_ContainsLayoutKey(string key)
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
    public void RemovedLayoutKeys_AreGoneInEveryCulture(string cultureName)
    {
        ResourceManager resourceManager = new("Foundry.Connect.Strings.Resources", typeof(App).Assembly);
        ResourceSet resourceSet = resourceManager.GetResourceSet(CultureInfo.GetCultureInfo(cultureName), createIfNotExists: true, tryParents: false)!;

        foreach (string key in new[]
        {
            "Ethernet.CheckCable", "Ethernet.Dot1xProvisioningFailed", "Ethernet.NoAdapterDetected", "Ethernet.WaitingDhcp", "Notice.CaptivePortalMessage",
            "Notice.CaptivePortalTitle", "Status.CheckingDescription", "Status.NetworkReadyDescription", "Wifi.ActionProvisionedLocked",
            "Wifi.ActionProvisionedSecretUnavailable", "Wifi.AuthenticationLabel", "Wifi.NoAdapterAvailable", "Wifi.NoAdapterDetected", "Wifi.ProfileLabel",
            "Wifi.ProvisionedUnavailableTitle", "Wifi.SelectNetworkHint", "Wifi.UnavailableAtRuntime"
        })
        {
            Assert.Null(resourceSet.GetString(key));
        }
    }

    [Theory]
    [MemberData(nameof(BannerTitleAndLabelKeys))]
    public void EveryCulture_ContainsBannerTitleOrLabelKey(string key)
    {
        ResourceManager resourceManager = new("Foundry.Connect.Strings.Resources", typeof(App).Assembly);

        Assert.All(CultureNames, cultureName =>
        {
            ResourceSet resourceSet = resourceManager.GetResourceSet(CultureInfo.GetCultureInfo(cultureName), createIfNotExists: true, tryParents: false)!;
            Assert.False(string.IsNullOrWhiteSpace(resourceSet.GetString(key)), $"{cultureName} {key}");
        });
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
            "Status.BlockedCaptivePortalTitle", "Status.BlockedTitle"
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
