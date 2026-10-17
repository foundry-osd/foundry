// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;

namespace Foundry.Utilities.Globalization;

/// <summary>
/// Describes one Windows keyboard layout that can be activated as an input locale.
/// </summary>
/// <param name="Id">Keyboard layout identifier (KLID) as eight uppercase hexadecimal digits, for example <c>00000813</c>.</param>
/// <param name="BaseLanguage">Two-letter base language the layout is grouped under, for example <c>fr</c> or <c>nl</c>.</param>
/// <param name="InputLanguageId">Language identifier paired with the layout when it is activated.</param>
/// <param name="LayoutFile">Layout DLL file name under <c>Windows\System32</c>.</param>
/// <param name="DisplayName">Localized layout name, or a fallback when Windows cannot resolve one.</param>
public sealed record KeyboardLayout(string Id, string BaseLanguage, int InputLanguageId, string LayoutFile, string DisplayName)
{
    /// <summary>
    /// Gets the <c>language:layout</c> pair accepted by DISM <c>/Set-InputLocale</c> and <c>wpeutil SetKeyboardLayout</c>.
    /// </summary>
    public string InputLocale => $"{InputLanguageId:x4}:{Id}";

    /// <summary>
    /// Gets a short uppercase label for menus: the region of the input language, or the base language when the culture has no region.
    /// </summary>
    public string ShortName
    {
        get
        {
            string cultureName = CultureInfo.GetCultureInfo(InputLanguageId).Name;
            int separator = cultureName.LastIndexOf('-');
            return separator >= 0 && separator < cultureName.Length - 1
                ? cultureName[(separator + 1)..].ToUpperInvariant()
                : BaseLanguage.ToUpperInvariant();
        }
    }
}
