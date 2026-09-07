// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeHelperTests
{
    [Theory]
    [InlineData(" fr_FR ", "fr-fr")]
    [InlineData("EN-us", "en-us")]
    [InlineData("", "")]
    public void NormalizeLanguageCode_ReturnsLowercaseComparisonValue(string input, string expected)
    {
        string normalized = WinPeLanguageUtility.Normalize(input);

        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(" fr_FR ", "fr-FR")]
    [InlineData("EN-us", "en-US")]
    [InlineData("invalid-culture-code", "invalid-culture-code")]
    public void Canonicalize_ReturnsCultureInfoNameWhenCultureExists(string input, string expected)
    {
        string canonical = WinPeLanguageUtility.Canonicalize(input);

        Assert.Equal(expected, canonical);
    }

    [Theory]
    [InlineData("es-ES")]
    [InlineData("fr-CA")]
    [InlineData("en-US")]
    public void TryResolveInputLocale_WhenCultureExists_ReturnsCanonicalCodeAndLocale(string language)
    {
        bool success = WinPeLanguageUtility.TryResolveInputLocale(language, out string canonicalLanguageCode, out string inputLocale);

        Assert.True(success);
        Assert.Equal(language, canonicalLanguageCode);
        Assert.Equal(language, inputLocale);
    }

    [Theory]
    [InlineData("invalid-culture-code")]
    [InlineData("en")]
    [InlineData("")]
    [InlineData("zz-ZZ")]
    public void TryResolveInputLocale_WhenCultureIsUnknown_ReturnsFalse(string language)
    {
        bool success = WinPeLanguageUtility.TryResolveInputLocale(language, out string canonicalLanguageCode, out string inputLocale);

        Assert.False(success);
        Assert.Equal(language, canonicalLanguageCode);
        Assert.Equal(string.Empty, inputLocale);
    }

    [Theory]
    [InlineData(WinPeArchitecture.X64, "amd64", "bootx64.efi", "win-x64", "x64")]
    [InlineData(WinPeArchitecture.Arm64, "arm64", "bootaa64.efi", "win-arm64", "arm64")]
    public void ArchitectureMappings_ReturnExpectedValues(
        WinPeArchitecture architecture,
        string expectedCopypeArchitecture,
        string expectedBootEfiName,
        string expectedRuntimeIdentifier,
        string expectedSevenZipFolder)
    {
        Assert.Equal(expectedCopypeArchitecture, architecture.ToCopypeArchitecture());
        Assert.Equal(expectedBootEfiName, architecture.ToBootEfiName());
        Assert.Equal(expectedRuntimeIdentifier, architecture.ToDotnetRuntimeIdentifier());
        Assert.Equal(expectedSevenZipFolder, architecture.ToSevenZipRuntimeFolder());
    }

}
