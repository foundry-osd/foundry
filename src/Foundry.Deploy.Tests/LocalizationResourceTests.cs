// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Resources;
using Foundry.Deploy.Services.Localization;

namespace Foundry.Deploy.Tests;

public sealed class LocalizationResourceTests
{
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
        CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);

        ResourceSet? resourceSet = LocalizationText.ResourceManager.GetResourceSet(
            culture,
            createIfNotExists: true,
            tryParents: false);

        Assert.NotNull(resourceSet);
        Assert.Equal("Foundry Deploy", resourceSet.GetString("App.Name"));
    }

    [Theory]
    [MemberData(nameof(SatelliteCultures))]
    public void SatelliteResourceSet_ContainsDeploymentValidationMessages(string cultureName)
    {
        ResourceSet resourceSet = Assert.IsAssignableFrom<ResourceSet>(
            LocalizationText.ResourceManager.GetResourceSet(
                CultureInfo.GetCultureInfo(cultureName),
                createIfNotExists: true,
                tryParents: false));
        string[] keys =
        [
            "Deployment.DiskPartFailureFormat",
            "Deployment.LayoutValidationProcessFailureFormat",
            "Deployment.LayoutValidationInvalidDataFormat",
            "Deployment.LayoutNotGptFormat",
            "Deployment.LayoutInvalidEfiFormat",
            "Deployment.LayoutInvalidWindowsFormat",
            "Deployment.LayoutInvalidRecoveryFormat",
            "Deployment.BcdBootMissingFormat",
            "Deployment.BcdTemplateMissingFormat",
            "Deployment.EfiUnavailableFormat",
            "Deployment.EfiNotWritableFormat",
            "Deployment.BcdBootFailure"
        ];

        foreach (string key in keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(resourceSet.GetString(key)));
        }
    }
}
