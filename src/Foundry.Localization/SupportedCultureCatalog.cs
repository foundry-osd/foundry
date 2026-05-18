using System.Globalization;

namespace Foundry.Localization;

/// <summary>
/// Defines the UI cultures supported by Foundry desktop applications.
/// </summary>
public static class SupportedCultureCatalog
{
    /// <summary>
    /// Gets the default UI culture code used when no supported culture is selected.
    /// </summary>
    public const string DefaultCultureCode = "en-US";

    private static readonly SupportedCultureDefinition[] Definitions =
    [
        new("en-US", "Language.English", 10),
        new("fr-FR", "Language.French", 20)
    ];

    /// <summary>
    /// Creates language selection options using the provided localized display name resolver.
    /// </summary>
    /// <param name="currentCulture">Currently active UI culture.</param>
    /// <param name="getString">Function that resolves display name resource keys.</param>
    /// <returns>Supported cultures sorted for display with the current culture marked as selected.</returns>
    public static IReadOnlyList<SupportedCultureOption> CreateOptions(
        CultureInfo currentCulture,
        Func<string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(currentCulture);
        ArgumentNullException.ThrowIfNull(getString);

        string selectedCode = NormalizeForComparison(currentCulture.Name);
        return Definitions
            .OrderBy(definition => definition.SortOrder)
            .ThenBy(definition => definition.Code, StringComparer.OrdinalIgnoreCase)
            .Select(definition => new SupportedCultureOption(
                Canonicalize(definition.Code),
                getString(definition.ResourceKey),
                NormalizeForComparison(definition.Code).Equals(selectedCode, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    /// <summary>
    /// Returns a canonical supported culture code, or the default culture when the input is unsupported.
    /// </summary>
    /// <param name="cultureCode">Culture code to validate.</param>
    /// <returns>A canonical supported culture code.</returns>
    public static string ValidateOrDefault(string? cultureCode)
    {
        if (string.IsNullOrWhiteSpace(cultureCode))
        {
            return DefaultCultureCode;
        }

        try
        {
            string canonicalCode = Canonicalize(cultureCode);
            return Definitions.Any(definition => string.Equals(
                Canonicalize(definition.Code),
                canonicalCode,
                StringComparison.OrdinalIgnoreCase))
                ? canonicalCode
                : DefaultCultureCode;
        }
        catch (CultureNotFoundException)
        {
            return DefaultCultureCode;
        }
    }

    /// <summary>
    /// Selects the first supported culture that matches a preferred culture list.
    /// </summary>
    /// <param name="preferredCultureCodes">Preferred culture codes in priority order.</param>
    /// <returns>The best supported culture code, or the default culture when no preference matches.</returns>
    public static string MatchPreferredCulture(IEnumerable<string?> preferredCultureCodes)
    {
        ArgumentNullException.ThrowIfNull(preferredCultureCodes);

        foreach (string? cultureCode in preferredCultureCodes)
        {
            string? directMatch = TryGetSupportedCultureCode(cultureCode);
            if (directMatch is not null)
            {
                return directMatch;
            }

            string? languageFamilyMatch = TryGetSupportedLanguageFamilyCode(cultureCode);
            if (languageFamilyMatch is not null)
            {
                return languageFamilyMatch;
            }
        }

        return DefaultCultureCode;
    }

    private static string? TryGetSupportedCultureCode(string? cultureCode)
    {
        if (string.IsNullOrWhiteSpace(cultureCode))
        {
            return null;
        }

        try
        {
            string canonicalCode = Canonicalize(cultureCode);
            return Definitions
                .Select(definition => Canonicalize(definition.Code))
                .FirstOrDefault(code => string.Equals(code, canonicalCode, StringComparison.OrdinalIgnoreCase));
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static string? TryGetSupportedLanguageFamilyCode(string? cultureCode)
    {
        if (string.IsNullOrWhiteSpace(cultureCode))
        {
            return null;
        }

        try
        {
            string languageFamily = CultureInfo.GetCultureInfo(cultureCode.Trim().Replace('_', '-')).TwoLetterISOLanguageName;
            return Definitions
                .Select(definition => Canonicalize(definition.Code))
                .FirstOrDefault(code => CultureInfo.GetCultureInfo(code).TwoLetterISOLanguageName.Equals(
                    languageFamily,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static string Canonicalize(string cultureCode)
    {
        return CultureInfo.GetCultureInfo(cultureCode.Trim().Replace('_', '-')).Name;
    }

    private static string NormalizeForComparison(string cultureCode)
    {
        return cultureCode.Trim().Replace('_', '-').ToLowerInvariant();
    }

    private sealed record SupportedCultureDefinition(string Code, string ResourceKey, int SortOrder);
}
