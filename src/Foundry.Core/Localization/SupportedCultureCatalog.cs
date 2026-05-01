using System.Globalization;

namespace Foundry.Core.Localization;

public static class SupportedCultureCatalog
{
    public const string DefaultCultureCode = "en-US";

    private static readonly SupportedCultureDefinition[] Definitions =
    [
        new("en-US", "Language.English", 10),
        new("fr-FR", "Language.French", 20)
    ];

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
