using System.Globalization;
using System.Resources;
using Foundry.Connect.Services.Localization;

namespace Foundry.Connect.Tests;

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
        ResourceManager resourceManager = new(
            "Foundry.Connect.Strings.Resources",
            typeof(LocalizationService).Assembly);
        CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);

        ResourceSet? resourceSet = resourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);

        Assert.NotNull(resourceSet);
        Assert.Equal("Foundry Connect", resourceSet.GetString("App.Name"));
    }
}
