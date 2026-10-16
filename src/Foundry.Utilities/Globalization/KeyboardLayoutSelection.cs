// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;

namespace Foundry.Utilities.Globalization;

/// <summary>
/// Layouts offered by a keyboard menu: the active layout's language first, then every other language.
/// </summary>
/// <param name="Suggested">Layouts of the active layout's base language; empty when the active layout is unknown.</param>
/// <param name="OtherLanguages">Every other base language with its layouts.</param>
internal sealed record KeyboardLayoutMenu(IReadOnlyList<KeyboardLayout> Suggested, IReadOnlyList<KeyboardLanguageGroup> OtherLanguages);

/// <summary>
/// Layouts of one base language.
/// </summary>
/// <param name="BaseLanguage">Two-letter base language.</param>
/// <param name="DisplayName">Native language name with an uppercase first letter.</param>
/// <param name="Layouts">Layouts of the language.</param>
internal sealed record KeyboardLanguageGroup(string BaseLanguage, string DisplayName, IReadOnlyList<KeyboardLayout> Layouts);

/// <summary>
/// One entry of a data-bound keyboard menu: a selectable layout, or a group whose children are entries.
/// </summary>
/// <param name="LayoutId">KLID of a selectable layout, or <see langword="null"/> for a group.</param>
/// <param name="Header">Text shown in the menu.</param>
/// <param name="IsSelected">Whether this layout is the active one.</param>
/// <param name="Children">Child entries of a group; empty for a layout.</param>
public sealed record KeyboardMenuEntry(string? LayoutId, string Header, bool IsSelected, IReadOnlyList<KeyboardMenuEntry> Children)
{
    /// <summary>
    /// Gets whether the entry is a layout that can be checked.
    /// </summary>
    public bool IsCheckable => LayoutId is not null;
}

/// <summary>
/// Selection rules shared by the authoring keyboard setting and the WinPE keyboard menus.
/// </summary>
public static class KeyboardLayoutSelection
{
    /// <summary>
    /// Returns the base languages that can be offered as keyboard languages: every layout language plus the WinPE base language.
    /// </summary>
    public static IReadOnlyList<string> GetLanguages(IReadOnlyList<KeyboardLayout> layouts, string winPeLanguage)
    {
        ArgumentNullException.ThrowIfNull(layouts);

        return layouts
            .Select(layout => layout.BaseLanguage)
            .Append(KeyboardLayoutCatalog.GetBaseLanguage(winPeLanguage))
            .Where(language => !string.IsNullOrEmpty(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Returns the keyboard language to show: the stored layout's language when it is installed, otherwise the WinPE base language.
    /// A stored layout keeps its language when the WinPE language changes.
    /// </summary>
    public static string ResolveLanguage(IReadOnlyList<KeyboardLayout> layouts, string? storedLayoutId, string winPeLanguage)
    {
        return Find(layouts, storedLayoutId)?.BaseLanguage ?? KeyboardLayoutCatalog.GetBaseLanguage(winPeLanguage);
    }

    /// <summary>
    /// Returns whether the "Default" entry applies: DISM only has a meaningful default for the WinPE language itself.
    /// </summary>
    public static bool AllowsDefault(string keyboardLanguage, string winPeLanguage)
    {
        return string.Equals(keyboardLanguage, KeyboardLayoutCatalog.GetBaseLanguage(winPeLanguage), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the layouts of one base language, in catalog order.
    /// </summary>
    public static IReadOnlyList<KeyboardLayout> GetLayouts(IReadOnlyList<KeyboardLayout> layouts, string keyboardLanguage)
    {
        ArgumentNullException.ThrowIfNull(layouts);

        return layouts
            .Where(layout => string.Equals(layout.BaseLanguage, keyboardLanguage, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <summary>
    /// Finds an installed layout by KLID, ignoring case and surrounding whitespace.
    /// </summary>
    public static KeyboardLayout? Find(IReadOnlyList<KeyboardLayout> layouts, string? layoutId)
    {
        ArgumentNullException.ThrowIfNull(layouts);

        return KeyboardLayoutCatalog.TryNormalizeId(layoutId, out string id)
            ? layouts.FirstOrDefault(layout => layout.Id == id)
            : null;
    }

    /// <summary>
    /// Builds a keyboard menu around the active layout.
    /// </summary>
    internal static KeyboardLayoutMenu BuildMenu(IReadOnlyList<KeyboardLayout> layouts, string? activeLayoutId)
    {
        ArgumentNullException.ThrowIfNull(layouts);

        string activeLanguage = Find(layouts, activeLayoutId)?.BaseLanguage ?? string.Empty;
        IReadOnlyList<KeyboardLayout> suggested = GetLayouts(layouts, activeLanguage);
        KeyboardLanguageGroup[] otherLanguages = layouts
            .Where(layout => !string.Equals(layout.BaseLanguage, activeLanguage, StringComparison.OrdinalIgnoreCase))
            .GroupBy(layout => layout.BaseLanguage, StringComparer.OrdinalIgnoreCase)
            .Select(group => new KeyboardLanguageGroup(group.Key, GetLanguageDisplayName(group.Key), group.ToArray()))
            .OrderBy(group => group.BaseLanguage, StringComparer.Ordinal)
            .ToArray();

        return new KeyboardLayoutMenu(suggested, otherLanguages);
    }

    /// <summary>
    /// Builds hierarchical menu entries: the active language's layouts, then one group holding every other language.
    /// The group is omitted when there is no other language.
    /// </summary>
    public static IReadOnlyList<KeyboardMenuEntry> BuildMenuEntries(
        IReadOnlyList<KeyboardLayout> layouts,
        string? activeLayoutId,
        string otherLanguagesHeader)
    {
        KeyboardLayoutMenu menu = BuildMenu(layouts, activeLayoutId);
        string activeId = Find(layouts, activeLayoutId)?.Id ?? string.Empty;
        var entries = menu.Suggested.Select(layout => CreateLayoutEntry(layout, activeId)).ToList();
        if (menu.OtherLanguages.Count > 0)
        {
            KeyboardMenuEntry[] languages = menu.OtherLanguages
                .Select(group => new KeyboardMenuEntry(
                    null,
                    group.DisplayName,
                    false,
                    group.Layouts.Select(layout => CreateLayoutEntry(layout, activeId)).ToArray()))
                .ToArray();
            entries.Add(new KeyboardMenuEntry(null, otherLanguagesHeader, false, languages));
        }

        return entries;
    }

    private static KeyboardMenuEntry CreateLayoutEntry(KeyboardLayout layout, string activeId)
    {
        return new KeyboardMenuEntry(layout.Id, layout.DisplayName, layout.Id == activeId, []);
    }

    /// <summary>
    /// Returns the native name of a base language with an uppercase first letter, or the language code when it is unknown.
    /// </summary>
    public static string GetLanguageDisplayName(string baseLanguage)
    {
        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(baseLanguage);
            string name = culture.NativeName;
            return string.IsNullOrEmpty(name)
                ? baseLanguage
                : culture.TextInfo.ToUpper(name[0]) + name[1..];
        }
        catch (CultureNotFoundException)
        {
            return baseLanguage;
        }
    }
}
