using System.Globalization;
using Foundry.Core.Localization;

namespace Foundry.Core.Tests.Localization;

public sealed class SupportedCultureCatalogTests
{
    [Fact]
    public void CreateOptions_ReturnsCanonicalCodesWithSingleSelectedCulture()
    {
        IReadOnlyList<SupportedCultureOption> options = SupportedCultureCatalog.CreateOptions(
            CultureInfo.GetCultureInfo("fr-FR"),
            key => key);

        Assert.NotEmpty(options);
        Assert.Equal(options.Count, options.Select(option => option.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(options, option => Assert.Equal(CultureInfo.GetCultureInfo(option.Code).Name, option.Code));
        Assert.Contains(options, option => option.Code == "en-US");
        Assert.Contains(options, option => option.Code == "fr-FR");
        Assert.Single(options, option => option.IsSelected);
        Assert.True(options.Single(option => option.Code == "fr-FR").IsSelected);
    }

    [Theory]
    [InlineData(null, "en-US")]
    [InlineData("", "en-US")]
    [InlineData("invalid", "en-US")]
    [InlineData("de-DE", "en-US")]
    [InlineData("fr_fr", "fr-FR")]
    [InlineData("EN-us", "en-US")]
    public void ValidateOrDefault_ReturnsSupportedCanonicalCulture(string? cultureCode, string expectedCode)
    {
        string result = SupportedCultureCatalog.ValidateOrDefault(cultureCode);

        Assert.Equal(expectedCode, result);
    }

    [Theory]
    [InlineData("fr-FR", "fr-FR")]
    [InlineData("fr-CA", "fr-FR")]
    [InlineData("en-GB", "en-US")]
    [InlineData("de-DE", "en-US")]
    [InlineData("invalid", "en-US")]
    public void MatchPreferredCulture_ReturnsBestSupportedCulture(string preferredCultureCode, string expectedCode)
    {
        string result = SupportedCultureCatalog.MatchPreferredCulture([preferredCultureCode]);

        Assert.Equal(expectedCode, result);
    }

    [Fact]
    public void MatchPreferredCulture_UsesFirstSupportedPreferredCulture()
    {
        string result = SupportedCultureCatalog.MatchPreferredCulture(["de-DE", "fr-CA", "en-GB"]);

        Assert.Equal("fr-FR", result);
    }
}
