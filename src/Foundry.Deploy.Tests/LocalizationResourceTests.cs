// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Resources;
using Foundry.Deploy.Services.Localization;

namespace Foundry.Deploy.Tests;

public sealed class LocalizationResourceTests
{
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
    }
}
