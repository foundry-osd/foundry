// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Globalization;

namespace Foundry.Utilities.Tests.Globalization;

public sealed class KeyboardLayoutSelectionTests
{
    private static readonly IReadOnlyList<KeyboardLayout> Layouts = KeyboardLayoutCatalog.FromEntries(
    [
        new KeyboardLayoutRegistryEntry("0000040C", "KBDFR.DLL", "French (Legacy, AZERTY)", null),
        new KeyboardLayoutRegistryEntry("0000080C", "KBDBE.DLL", "Belgian French", null),
        new KeyboardLayoutRegistryEntry("00000413", "KBDNE.DLL", "Dutch", null),
        new KeyboardLayoutRegistryEntry("00000813", "KBDBE.DLL", "Belgian (Period)", null)
    ]);

    [Fact]
    public void ResolveLanguage_UsesStoredLayoutLanguage()
    {
        Assert.Equal("nl", KeyboardLayoutSelection.ResolveLanguage(Layouts, "00000813", "en-US"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000407")]
    public void ResolveLanguage_FallsBackToWinPeLanguage(string? storedLayoutId)
    {
        Assert.Equal("fr", KeyboardLayoutSelection.ResolveLanguage(Layouts, storedLayoutId, "fr-FR"));
    }

    [Fact]
    public void ResolveLanguage_KeepsStoredLayoutWhenWinPeLanguageChanges()
    {
        Assert.Equal("fr", KeyboardLayoutSelection.ResolveLanguage(Layouts, "0000080C", "fr-FR"));
        Assert.Equal("fr", KeyboardLayoutSelection.ResolveLanguage(Layouts, "0000080C", "en-US"));
    }

    [Theory]
    [InlineData("00000407", true)]
    [InlineData("0000080C", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsUnavailable_OnlyForStoredLayoutsThatAreNotInstalled(string? storedLayoutId, bool expected)
    {
        Assert.Equal(expected, KeyboardLayoutSelection.IsUnavailable(Layouts, storedLayoutId));
    }

    [Theory]
    [InlineData("fr", "fr-FR", true)]
    [InlineData("fr", "fr-CA", true)]
    [InlineData("nl", "en-US", false)]
    public void AllowsDefault_OnlyForWinPeBaseLanguage(string keyboardLanguage, string winPeLanguage, bool expected)
    {
        Assert.Equal(expected, KeyboardLayoutSelection.AllowsDefault(keyboardLanguage, winPeLanguage));
    }

    [Fact]
    public void GetLanguages_IncludesWinPeLanguageWithoutLayouts()
    {
        IReadOnlyList<string> languages = KeyboardLayoutSelection.GetLanguages(Layouts, "ja-JP");

        Assert.Equal(["fr", "ja", "nl"], languages);
    }

    [Fact]
    public void GetLayouts_ReturnsOnlyLayoutsOfLanguage()
    {
        IReadOnlyList<KeyboardLayout> layouts = KeyboardLayoutSelection.GetLayouts(Layouts, "nl");

        Assert.Equal(["00000813", "00000413"], layouts.Select(layout => layout.Id));
    }

    [Fact]
    public void BuildMenu_PutsActiveLanguageFirst()
    {
        KeyboardLayoutMenu menu = KeyboardLayoutSelection.BuildMenu(Layouts, "0000080c");

        Assert.All(menu.Suggested, layout => Assert.Equal("fr", layout.BaseLanguage));
        Assert.Equal(2, menu.Suggested.Count);
        KeyboardLanguageGroup other = Assert.Single(menu.OtherLanguages);
        Assert.Equal("nl", other.BaseLanguage);
        Assert.Equal("Nederlands", other.DisplayName);
    }

    [Fact]
    public void BuildMenuEntries_ListsSuggestedLayoutsThenOtherLanguages()
    {
        IReadOnlyList<KeyboardMenuEntry> entries = KeyboardLayoutSelection.BuildMenuEntries(Layouts, "0000040C", "Other languages");

        Assert.Equal(["0000080C", "0000040C", null], entries.Select(entry => entry.LayoutId));
        Assert.True(entries[1].IsSelected);
        Assert.False(entries[0].IsSelected);
        KeyboardMenuEntry other = entries[2];
        Assert.Equal("Other languages", other.Header);
        Assert.False(other.IsCheckable);
        KeyboardMenuEntry dutch = Assert.Single(other.Children);
        Assert.Equal("Nederlands", dutch.Header);
        Assert.Equal(["00000813", "00000413"], dutch.Children.Select(entry => entry.LayoutId));
    }

    [Fact]
    public void BuildMenuEntries_WhenNoOtherLanguage_OmitsOtherLanguagesEntry()
    {
        IReadOnlyList<KeyboardMenuEntry> entries = KeyboardLayoutSelection.BuildMenuEntries(
            KeyboardLayoutSelection.GetLayouts(Layouts, "fr"),
            "0000040C",
            "Other languages");

        Assert.All(entries, entry => Assert.NotNull(entry.LayoutId));
    }

    [Fact]
    public void BuildMenu_WhenActiveLayoutIsUnknown_ListsEveryLanguageAsOther()
    {
        KeyboardLayoutMenu menu = KeyboardLayoutSelection.BuildMenu(Layouts, "E0010411");

        Assert.Empty(menu.Suggested);
        Assert.Equal(["fr", "nl"], menu.OtherLanguages.Select(group => group.BaseLanguage));
    }
}
