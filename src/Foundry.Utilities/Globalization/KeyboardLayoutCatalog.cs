// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Foundry.Utilities.Globalization;

/// <summary>
/// Builds the list of Windows keyboard layouts grouped by base language.
/// </summary>
public static class KeyboardLayoutCatalog
{
    private const string KeyboardLayoutsRegistryPath = @"SYSTEM\CurrentControlSet\Control\Keyboard Layouts";
    private const int DisplayNameBufferLength = 256;

    // Layouts whose KLID language part is not the language Windows pairs them with.
    private static readonly Dictionary<string, (string BaseLanguage, int InputLanguageId)> LanguageOverrides =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["00001009"] = ("fr", 0x0C0C)
        };

    /// <summary>
    /// Converts raw registry entries into keyboard layouts, skipping entries without a valid KLID, language or layout file.
    /// </summary>
    /// <param name="entries">Raw registry entries.</param>
    /// <returns>Layouts sorted by base language, then display name.</returns>
    public static IReadOnlyList<KeyboardLayout> FromEntries(IEnumerable<KeyboardLayoutRegistryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var layouts = new List<KeyboardLayout>();
        foreach (KeyboardLayoutRegistryEntry entry in entries)
        {
            if (TryCreateLayout(entry, out KeyboardLayout? layout))
            {
                layouts.Add(layout);
            }
        }

        return layouts
            .OrderBy(layout => layout.BaseLanguage, StringComparer.Ordinal)
            .ThenBy(layout => layout.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static readonly Lazy<IReadOnlyList<KeyboardLayout>> Installed = new(LoadInstalled);

    /// <summary>
    /// Gets the installed keyboard layouts, read once per process. Layouts do not change while Foundry runs,
    /// and each read resolves every localized layout name, so repeated callers share this list.
    /// </summary>
    /// <returns>The cached installed layouts.</returns>
    public static IReadOnlyList<KeyboardLayout> GetInstalled()
    {
        return Installed.Value;
    }

    /// <summary>
    /// Reads the keyboard layouts installed on the running Windows or Windows PE system.
    /// </summary>
    /// <returns>The installed layouts, or an empty list when the registry cannot be read.</returns>
    public static IReadOnlyList<KeyboardLayout> LoadInstalled()
    {
        using RegistryKey? root = Registry.LocalMachine.OpenSubKey(KeyboardLayoutsRegistryPath);
        if (root is null)
        {
            return [];
        }

        var entries = new List<KeyboardLayoutRegistryEntry>();
        foreach (string id in root.GetSubKeyNames())
        {
            using RegistryKey? key = root.OpenSubKey(id);
            if (key is null)
            {
                continue;
            }

            entries.Add(new KeyboardLayoutRegistryEntry(
                id,
                key.GetValue("Layout File") as string,
                key.GetValue("Layout Text") as string,
                ResolveIndirectString(key.GetValue("Layout Display Name") as string)));
        }

        return FromEntries(entries);
    }

    /// <summary>
    /// Validates a KLID and returns it as eight uppercase hexadecimal digits.
    /// </summary>
    /// <param name="value">Candidate KLID.</param>
    /// <param name="id">Normalized KLID when valid; otherwise an empty string.</param>
    /// <returns><see langword="true"/> when the value is a valid KLID.</returns>
    public static bool TryNormalizeId(string? value, out string id)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 8 && uint.TryParse(trimmed, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
        {
            id = trimmed.ToUpperInvariant();
            return true;
        }

        id = string.Empty;
        return false;
    }

    /// <summary>
    /// Returns the two-letter base language of a culture name, for example <c>fr</c> for <c>fr-BE</c>.
    /// </summary>
    /// <param name="cultureName">Culture name such as a WinPE language.</param>
    /// <returns>The base language, or an empty string when the culture is unknown.</returns>
    public static string GetBaseLanguage(string cultureName)
    {
        string canonical = CultureCode.Canonicalize(cultureName);
        if (string.IsNullOrEmpty(canonical))
        {
            return string.Empty;
        }

        try
        {
            return CultureInfo.GetCultureInfo(canonical).TwoLetterISOLanguageName;
        }
        catch (CultureNotFoundException)
        {
            return string.Empty;
        }
    }

    private static bool TryCreateLayout(KeyboardLayoutRegistryEntry entry, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out KeyboardLayout? layout)
    {
        layout = null;
        if (!TryNormalizeId(entry.Id, out string id) || string.IsNullOrWhiteSpace(entry.LayoutFile))
        {
            return false;
        }

        string baseLanguage;
        int inputLanguageId;
        if (LanguageOverrides.TryGetValue(id, out (string BaseLanguage, int InputLanguageId) languageOverride))
        {
            (baseLanguage, inputLanguageId) = languageOverride;
        }
        else
        {
            inputLanguageId = int.Parse(id[4..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            if (!TryGetBaseLanguage(inputLanguageId, out baseLanguage))
            {
                return false;
            }
        }

        string displayName = FirstNonEmpty(entry.DisplayName, entry.LayoutText) ?? id;
        layout = new KeyboardLayout(id, baseLanguage, inputLanguageId, entry.LayoutFile.Trim(), displayName);
        return true;
    }

    private static bool TryGetBaseLanguage(int languageId, out string baseLanguage)
    {
        baseLanguage = string.Empty;
        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(languageId);
            if (culture.Equals(CultureInfo.InvariantCulture) || string.IsNullOrEmpty(culture.TwoLetterISOLanguageName))
            {
                return false;
            }

            baseLanguage = culture.TwoLetterISOLanguageName;
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static string? ResolveIndirectString(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var buffer = new StringBuilder(DisplayNameBufferLength);
        return SHLoadIndirectString(source, buffer, buffer.Capacity, IntPtr.Zero) == 0
            ? buffer.ToString()
            : null;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SHLoadIndirectString(string source, StringBuilder output, int outputLength, IntPtr reserved);
}
