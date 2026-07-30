// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Collections;
using System.Resources;
using System.Text.RegularExpressions;
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
    public void SatelliteResourceSet_HasSameKeysAndFormatArgumentsAsDefaultCulture(string cultureName)
    {
        ResourceSet baseline = Assert.IsAssignableFrom<ResourceSet>(
            LocalizationText.ResourceManager.GetResourceSet(
                CultureInfo.GetCultureInfo("en-US"),
                createIfNotExists: true,
                tryParents: false));
        ResourceSet localized = Assert.IsAssignableFrom<ResourceSet>(
            LocalizationText.ResourceManager.GetResourceSet(
                CultureInfo.GetCultureInfo(cultureName),
                createIfNotExists: true,
                tryParents: false));
        IReadOnlyDictionary<string, string> baselineValues = ReadValues(baseline);
        IReadOnlyDictionary<string, string> localizedValues = ReadValues(localized);

        Assert.Equal(baselineValues.Keys.Order(), localizedValues.Keys.Order());
        foreach ((string key, string value) in baselineValues)
        {
            Assert.Equal(
                ReadFormatArgumentIndexes(value),
                ReadFormatArgumentIndexes(localizedValues[key]));
        }
    }

    private static IReadOnlyDictionary<string, string> ReadValues(ResourceSet resourceSet)
    {
        return resourceSet
            .Cast<DictionaryEntry>()
            .ToDictionary(
                entry => Assert.IsType<string>(entry.Key),
                entry => Assert.IsType<string>(entry.Value),
                StringComparer.Ordinal);
    }

    private static string[] ReadFormatArgumentIndexes(string value)
    {
        return Regex.Matches(value, @"\{(?<index>\d+)(?:[^}]*)\}")
            .Select(match => match.Groups["index"].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
