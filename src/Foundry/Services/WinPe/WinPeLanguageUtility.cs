using System.Globalization;

namespace Foundry.Services.WinPe;

internal static class WinPeLanguageUtility
{
    public static string Normalize(string languageCode)
    {
        return string.IsNullOrWhiteSpace(languageCode)
            ? string.Empty
            : languageCode.Trim().Replace('_', '-').ToLowerInvariant();
    }

    public static bool TryResolveInputLocale(string languageCode, out string canonicalLanguageCode, out string inputLocale)
    {
        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(languageCode);
            canonicalLanguageCode = culture.Name;
            int keyboardLayoutId = culture.KeyboardLayoutId;
            string hex = keyboardLayoutId.ToString("x4", CultureInfo.InvariantCulture);
            inputLocale = $"{hex}:0000{hex}";
            return true;
        }
        catch (CultureNotFoundException)
        {
            canonicalLanguageCode = languageCode;
            inputLocale = string.Empty;
            return false;
        }
    }
}
