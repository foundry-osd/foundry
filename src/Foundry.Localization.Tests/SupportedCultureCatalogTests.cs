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
}
