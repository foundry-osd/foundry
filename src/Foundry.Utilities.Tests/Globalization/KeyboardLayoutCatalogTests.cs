// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Globalization;

namespace Foundry.Utilities.Tests.Globalization;

public sealed class KeyboardLayoutCatalogTests
{
    [Fact]
    public void FromEntries_GroupsByBaseLanguage()
    {
        IReadOnlyList<KeyboardLayout> layouts = KeyboardLayoutCatalog.FromEntries(
        [
            Entry("0000040C", "KBDFR.DLL", "French (Legacy, AZERTY)"),
            Entry("0000080C", "KBDBE.DLL", "Belgian French"),
            Entry("00000413", "KBDNE.DLL", "Dutch"),
            Entry("00000813", "KBDBE.DLL", "Belgian (Period)")
        ]);

        Assert.Equal("fr", Single(layouts, "0000040C").BaseLanguage);
        Assert.Equal("fr", Single(layouts, "0000080C").BaseLanguage);
        Assert.Equal("nl", Single(layouts, "00000413").BaseLanguage);
        KeyboardLayout belgianPeriod = Single(layouts, "00000813");
        Assert.Equal("nl", belgianPeriod.BaseLanguage);
        Assert.Equal("0813:00000813", belgianPeriod.InputLocale);
        Assert.Equal("BE", belgianPeriod.ShortName);
    }

    [Fact]
    public void FromEntries_AppliesOverrideForCanadianFrench()
    {
        KeyboardLayout layout = Assert.Single(KeyboardLayoutCatalog.FromEntries(
        [
            Entry("00001009", "KBDCA.DLL", "Canadian French")
        ]));

        Assert.Equal("fr", layout.BaseLanguage);
        Assert.Equal("0c0c:00001009", layout.InputLocale);
        Assert.Equal("CA", layout.ShortName);
    }

    [Fact]
    public void FromEntries_SkipsInvalidLanguageAndMissingLayoutFile()
    {
        IReadOnlyList<KeyboardLayout> layouts = KeyboardLayoutCatalog.FromEntries(
        [
            Entry("0000FFFF", "KBDXX.DLL", "Invalid language"),
            Entry("00000407", null, "German without layout file"),
            Entry("not-a-klid", "KBDXX.DLL", "Invalid identifier"),
            Entry("00000409", "KBDUS.DLL", "US")
        ]);

        KeyboardLayout layout = Assert.Single(layouts);
        Assert.Equal("00000409", layout.Id);
    }

    [Fact]
    public void FromEntries_FallsBackToLayoutTextThenId()
    {
        IReadOnlyList<KeyboardLayout> layouts = KeyboardLayoutCatalog.FromEntries(
        [
            new KeyboardLayoutRegistryEntry("0000040C", "KBDFR.DLL", "French (Legacy, AZERTY)", null),
            new KeyboardLayoutRegistryEntry("00000413", "KBDNE.DLL", null, null),
            new KeyboardLayoutRegistryEntry("00000809", "KBDUK.DLL", "United Kingdom", "Royaume-Uni")
        ]);

        Assert.Equal("French (Legacy, AZERTY)", Single(layouts, "0000040C").DisplayName);
        Assert.Equal("00000413", Single(layouts, "00000413").DisplayName);
        Assert.Equal("Royaume-Uni", Single(layouts, "00000809").DisplayName);
    }

    [Theory]
    [InlineData("00000813", "00000813")]
    [InlineData("0000080c", "0000080C")]
    [InlineData(" 0001040C ", "0001040C")]
    public void TryNormalizeId_AcceptsHexAndUppercases(string value, string expected)
    {
        Assert.True(KeyboardLayoutCatalog.TryNormalizeId(value, out string id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("813")]
    [InlineData("zz000813")]
    [InlineData("000008130")]
    public void TryNormalizeId_RejectsInvalidValues(string? value)
    {
        Assert.False(KeyboardLayoutCatalog.TryNormalizeId(value, out _));
    }

    [Theory]
    [InlineData("fr-FR", "fr")]
    [InlineData("nl-BE", "nl")]
    [InlineData("EN_us", "en")]
    public void GetBaseLanguage_ReturnsTwoLetterLanguage(string cultureName, string expected)
    {
        Assert.Equal(expected, KeyboardLayoutCatalog.GetBaseLanguage(cultureName));
    }

    private static KeyboardLayoutRegistryEntry Entry(string id, string? layoutFile, string layoutText)
    {
        return new KeyboardLayoutRegistryEntry(id, layoutFile, layoutText, null);
    }

    private static KeyboardLayout Single(IReadOnlyList<KeyboardLayout> layouts, string id)
    {
        return Assert.Single(layouts, layout => layout.Id == id);
    }
}
