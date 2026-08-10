// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Resources;
using Foundry.Connect.Services.Localization;

namespace Foundry.Connect.Tests;

public sealed class LocalizationResourceTests
{
    private static readonly string[] AvaloniaUiKeys =
    [
        "Menu.Help",
        "Tools.Diagnostics",
        "Action.Refresh",
        "Action.Close",
        "Diagnostics.Title",
        "Diagnostics.Loading",
        "Diagnostics.CaptureFailed",
        "Diagnostics.ApplicationVersion",
        "Diagnostics.Runtime",
        "Diagnostics.ProcessArchitecture",
        "Diagnostics.Configuration",
        "Diagnostics.RefreshInterval",
        "Diagnostics.LastUpdated",
        "Diagnostics.Pending",
        "Diagnostics.Readiness",
        "Diagnostics.ActiveConnection",
        "Diagnostics.None",
        "Diagnostics.Adapters",
        "Diagnostics.LastError",
        "Diagnostics.Captured"
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
    public void SatelliteResourceSet_IsAvailableForAdkCulture(string cultureName)
    {
        ResourceManager resourceManager = new(
            "Foundry.Connect.Strings.Resources",
            typeof(LocalizationService).Assembly);
        CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);

        ResourceSet? resourceSet = resourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);

        Assert.NotNull(resourceSet);
        Assert.Equal("Foundry Connect", resourceSet.GetString("App.Name"));
        foreach (string key in AvaloniaUiKeys)
        {
            Assert.False(string.IsNullOrWhiteSpace(resourceSet.GetString(key)), $"Missing localized resource '{key}' for '{cultureName}'.");
        }
    }
}
