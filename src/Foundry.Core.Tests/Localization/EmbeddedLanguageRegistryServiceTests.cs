using System.Globalization;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Localization;

public sealed class EmbeddedLanguageRegistryServiceTests
{
    [Fact]
    public void GetLanguages_ReturnsSortedCanonicalEntriesWithoutDuplicates()
    {
        var service = new EmbeddedLanguageRegistryService();

        IReadOnlyList<LanguageRegistryEntry> languages = service.GetLanguages();

        Assert.NotEmpty(languages);
        Assert.All(languages, language => Assert.False(string.IsNullOrWhiteSpace(language.Code)));
        Assert.All(languages, language => Assert.Equal(CultureInfo.GetCultureInfo(language.Code).Name, language.Code));
        Assert.All(languages, language => Assert.False(string.IsNullOrWhiteSpace(language.DisplayName)));
        Assert.All(languages, language => Assert.False(string.IsNullOrWhiteSpace(language.EnglishName)));

        LanguageRegistryEntry[] expectedOrder = languages
            .OrderBy(language => language.SortOrder)
            .ThenBy(language => language.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(expectedOrder, languages);
        Assert.Equal(
            languages.Count,
            languages.Select(language => language.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void GetLanguages_IncludesMicrosoftOperatingSystemCatalogLanguages()
    {
        var service = new EmbeddedLanguageRegistryService();

        IReadOnlyList<LanguageRegistryEntry> languages = service.GetLanguages();

        string[] expectedCodes =
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

        string[] actualCodes = languages
            .Select(language => language.Code)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(expectedCodes, actualCodes);
    }
}
