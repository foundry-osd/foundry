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
}
