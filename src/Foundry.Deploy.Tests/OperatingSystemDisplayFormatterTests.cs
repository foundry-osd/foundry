using System.Globalization;
using Foundry.Deploy.Converters;

namespace Foundry.Deploy.Tests;

public sealed class OperatingSystemDisplayFormatterTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    [Theory]
    [InlineData("RET", "Retail")]
    [InlineData("VOL", "Volume")]
    public void FormatLicenseChannel_UsesEnglishDisplayText(string input, string expected)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

        Assert.Equal(expected, OperatingSystemDisplayFormatter.FormatLicenseChannel(input));
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
    }
}
