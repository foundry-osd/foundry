using System.Globalization;
using Foundry.Localization;

namespace Foundry.Localization.Tests;

public sealed class SupportedCultureCatalogTests
{
    [Fact]
    public void CreateOptions_ReturnsCanonicalCodesWithSingleSelectedCulture()
    {
        SupportedCultureCatalog catalog = CreateCatalog();

        IReadOnlyList<SupportedCultureOption> options = catalog.CreateOptions(
            CultureInfo.GetCultureInfo("es-ES"),
            key => key);

        Assert.Equal(["Language.German", "Language.Spanish", "Language.Italian"], options.Select(option => option.DisplayName));
        Assert.Equal(options.Count, options.Select(option => option.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(options, option => Assert.Equal(CultureInfo.GetCultureInfo(option.Code).Name, option.Code));
        Assert.Single(options, option => option.IsSelected);
        Assert.True(options.Single(option => option.Code == "es-ES").IsSelected);
    }

    [Theory]
    [InlineData(null, "de-DE")]
    [InlineData("", "de-DE")]
    [InlineData("invalid", "de-DE")]
    [InlineData("fr-FR", "de-DE")]
    [InlineData("es_es", "es-ES")]
    [InlineData("IT-it", "it-IT")]
    public void ValidateOrDefault_ReturnsSupportedCanonicalCulture(string? cultureCode, string expectedCode)
    {
        SupportedCultureCatalog catalog = CreateCatalog();

        string result = catalog.ValidateOrDefault(cultureCode);

        Assert.Equal(expectedCode, result);
    }

    [Theory]
    [InlineData("es-ES", "es-ES")]
    [InlineData("es-MX", "es-ES")]
    [InlineData("it-CH", "it-IT")]
    [InlineData("fr-FR", "de-DE")]
    [InlineData("invalid", "de-DE")]
    public void MatchPreferredCulture_ReturnsBestSupportedCulture(string preferredCultureCode, string expectedCode)
    {
        SupportedCultureCatalog catalog = CreateCatalog();

        string result = catalog.MatchPreferredCulture([preferredCultureCode]);

        Assert.Equal(expectedCode, result);
    }

    [Fact]
    public void MatchPreferredCulture_UsesFirstSupportedPreferredCulture()
    {
        SupportedCultureCatalog catalog = CreateCatalog();

        string result = catalog.MatchPreferredCulture(["fr-FR", "it-CH", "es-MX"]);

        Assert.Equal("it-IT", result);
    }

    [Theory]
    [InlineData("en-AU", "en-US")]
    [InlineData("en-GB", "en-GB")]
    [InlineData("es-AR", "es-MX")]
    [InlineData("es-419", "es-MX")]
    [InlineData("es-CO", "es-MX")]
    [InlineData("es-UY", "es-MX")]
    [InlineData("es-ES", "es-ES")]
    [InlineData("fr-BE", "fr-FR")]
    [InlineData("fr-CH", "fr-FR")]
    [InlineData("fr-CA", "fr-CA")]
    [InlineData("pt-AO", "pt-PT")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("zh-HK", "zh-TW")]
    [InlineData("zh-Hant", "zh-TW")]
    [InlineData("zh-SG", "zh-CN")]
    [InlineData("zh-Hans", "zh-CN")]
    [InlineData("sr-Cyrl-RS", "sr-Latn-RS")]
    [InlineData("sr", "sr-Latn-RS")]
    [InlineData("no", "nb-NO")]
    [InlineData("no-NO", "nb-NO")]
    [InlineData("nn-NO", "nb-NO")]
    public void MatchPreferredCulture_UsesExplicitAdkFallbackPolicy(string preferredCultureCode, string expectedCode)
    {
        SupportedCultureCatalog catalog = CreateAdkCatalog();

        string result = catalog.MatchPreferredCulture([preferredCultureCode]);

        Assert.Equal(expectedCode, result);
    }

    [Fact]
    public void ValidateOrDefault_DoesNotUseLanguageFamilyFallback()
    {
        SupportedCultureCatalog catalog = CreateAdkCatalog();

        string result = catalog.ValidateOrDefault("fr-BE");

        Assert.Equal("en-US", result);
    }

    [Fact]
    public void FoundrySupportedCultures_IncludesAllAdkCultures()
    {
        SupportedCultureCatalog catalog = FoundrySupportedCultures.CreateCatalog();

        IReadOnlyList<SupportedCultureOption> options = catalog.CreateOptions(
            CultureInfo.GetCultureInfo(FoundrySupportedCultures.DefaultCultureCode),
            key => key);

        Assert.Equal(
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
            ],
            options.Select(option => option.Code));
    }

    [Fact]
    public void Constructor_WhenDefaultCultureIsNotConfigured_Throws()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new SupportedCultureCatalog(
            "pt-PT",
            [new SupportedCultureDefinition("es-ES", "Language.Spanish", 10)]));

        Assert.Contains("default", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SupportedCultureCatalog CreateCatalog()
    {
        return new SupportedCultureCatalog(
            "de-DE",
            [
                new SupportedCultureDefinition("es-ES", "Language.Spanish", 20),
                new SupportedCultureDefinition("de-DE", "Language.German", 10),
                new SupportedCultureDefinition("it-IT", "Language.Italian", 30)
            ]);
    }

    private static SupportedCultureCatalog CreateAdkCatalog()
    {
        return new SupportedCultureCatalog(
            "en-US",
            [
                new SupportedCultureDefinition("zh-TW", "Language.ChineseTraditional", 360),
                new SupportedCultureDefinition("es-MX", "Language.SpanishMexico", 100),
                new SupportedCultureDefinition("pt-BR", "Language.PortugueseBrazil", 250),
                new SupportedCultureDefinition("fr-CA", "Language.FrenchCanada", 130),
                new SupportedCultureDefinition("en-GB", "Language.EnglishUnitedKingdom", 70),
                new SupportedCultureDefinition("sr-Latn-RS", "Language.SerbianLatin", 310),
                new SupportedCultureDefinition("nb-NO", "Language.NorwegianBokmal", 220),
                new SupportedCultureDefinition("zh-CN", "Language.ChineseSimplified", 350),
                new SupportedCultureDefinition("es-ES", "Language.SpanishSpain", 90),
                new SupportedCultureDefinition("fr-FR", "Language.FrenchFrance", 140),
                new SupportedCultureDefinition("pt-PT", "Language.PortuguesePortugal", 260),
                new SupportedCultureDefinition("en-US", "Language.EnglishUnitedStates", 80)
            ]);
    }
}
