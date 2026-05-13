using System.Globalization;
using Foundry.Deploy.Converters;

namespace Foundry.Deploy.Tests;

public sealed class OperatingSystemDisplayFormatterTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    [Theory]
    [InlineData("RET", "Détail")]
    [InlineData("VOL", "Volume")]
    public void FormatLicenseChannel_UsesCurrentUiCulture(string input, string expected)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        Assert.Equal(expected, OperatingSystemDisplayFormatter.FormatLicenseChannel(input));
    }

    [Theory]
    [InlineData("Home", "Famille")]
    [InlineData("Home N", "Famille N")]
    [InlineData("Home Single Language", "Famille unilingue")]
    [InlineData("Education", "Éducation")]
    [InlineData("Enterprise", "Entreprise")]
    [InlineData("Pro", "Professionnel")]
    public void FormatEdition_UsesCurrentUiCultureForKnownEditions(string input, string expected)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        Assert.Equal(expected, OperatingSystemDisplayFormatter.FormatEdition(input));
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
    }
}
