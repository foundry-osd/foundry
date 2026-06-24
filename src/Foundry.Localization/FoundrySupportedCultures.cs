// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Localization;

/// <summary>
/// Creates the shared UI culture catalog used by Foundry desktop applications.
/// </summary>
public static class FoundrySupportedCultures
{
    /// <summary>
    /// Gets the default UI culture used by Foundry desktop applications.
    /// </summary>
    public const string DefaultCultureCode = "en-US";

    /// <summary>
    /// Creates the supported culture catalog for the current Foundry desktop resource set.
    /// </summary>
    /// <returns>A catalog containing the UI cultures currently shipped by the apps.</returns>
    public static SupportedCultureCatalog CreateCatalog()
    {
        return new SupportedCultureCatalog(
            DefaultCultureCode,
            [
                new SupportedCultureDefinition("ar-SA", "Language.ArabicSaudiArabia", 10),
                new SupportedCultureDefinition("bg-BG", "Language.BulgarianBulgaria", 20),
                new SupportedCultureDefinition("cs-CZ", "Language.CzechCzechia", 30),
                new SupportedCultureDefinition("da-DK", "Language.DanishDenmark", 40),
                new SupportedCultureDefinition("de-DE", "Language.GermanGermany", 50),
                new SupportedCultureDefinition("el-GR", "Language.GreekGreece", 60),
                new SupportedCultureDefinition("en-GB", "Language.EnglishUnitedKingdom", 70),
                new SupportedCultureDefinition(DefaultCultureCode, "Language.EnglishUnitedStates", 80),
                new SupportedCultureDefinition("es-ES", "Language.SpanishSpain", 90),
                new SupportedCultureDefinition("es-MX", "Language.SpanishMexico", 100),
                new SupportedCultureDefinition("et-EE", "Language.EstonianEstonia", 110),
                new SupportedCultureDefinition("fi-FI", "Language.FinnishFinland", 120),
                new SupportedCultureDefinition("fr-CA", "Language.FrenchCanada", 130),
                new SupportedCultureDefinition("fr-FR", "Language.FrenchFrance", 140),
                new SupportedCultureDefinition("he-IL", "Language.HebrewIsrael", 150),
                new SupportedCultureDefinition("hr-HR", "Language.CroatianCroatia", 160),
                new SupportedCultureDefinition("hu-HU", "Language.HungarianHungary", 170),
                new SupportedCultureDefinition("it-IT", "Language.ItalianItaly", 180),
                new SupportedCultureDefinition("ja-JP", "Language.JapaneseJapan", 190),
                new SupportedCultureDefinition("ko-KR", "Language.KoreanKorea", 200),
                new SupportedCultureDefinition("lt-LT", "Language.LithuanianLithuania", 210),
                new SupportedCultureDefinition("lv-LV", "Language.LatvianLatvia", 220),
                new SupportedCultureDefinition("nb-NO", "Language.NorwegianBokmalNorway", 230),
                new SupportedCultureDefinition("nl-NL", "Language.DutchNetherlands", 240),
                new SupportedCultureDefinition("pl-PL", "Language.PolishPoland", 250),
                new SupportedCultureDefinition("pt-BR", "Language.PortugueseBrazil", 260),
                new SupportedCultureDefinition("pt-PT", "Language.PortuguesePortugal", 270),
                new SupportedCultureDefinition("ro-RO", "Language.RomanianRomania", 280),
                new SupportedCultureDefinition("ru-RU", "Language.RussianRussia", 290),
                new SupportedCultureDefinition("sk-SK", "Language.SlovakSlovakia", 300),
                new SupportedCultureDefinition("sl-SI", "Language.SlovenianSlovenia", 310),
                new SupportedCultureDefinition("sr-Latn-RS", "Language.SerbianLatinSerbia", 320),
                new SupportedCultureDefinition("sv-SE", "Language.SwedishSweden", 330),
                new SupportedCultureDefinition("th-TH", "Language.ThaiThailand", 340),
                new SupportedCultureDefinition("tr-TR", "Language.TurkishTurkiye", 350),
                new SupportedCultureDefinition("uk-UA", "Language.UkrainianUkraine", 360),
                new SupportedCultureDefinition("zh-CN", "Language.ChineseSimplifiedChina", 370),
                new SupportedCultureDefinition("zh-TW", "Language.ChineseTraditionalTaiwan", 380)
            ]);
    }
}
