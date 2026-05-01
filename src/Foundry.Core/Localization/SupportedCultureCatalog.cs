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
