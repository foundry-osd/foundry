using System.Globalization;
using Foundry.Connect.ViewModels;

namespace Foundry.Connect.Tests;

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
}
