using System.Globalization;

namespace Foundry.Localization;

/// <summary>
/// Provides validation and display metadata for a configured set of UI cultures.
/// </summary>
public sealed class SupportedCultureCatalog
{
    private readonly SupportedCultureDefinition[] definitions;

    /// <summary>
    /// Initializes a new instance of the <see cref="SupportedCultureCatalog" /> class.
    /// </summary>
    /// <param name="defaultCultureCode">Default culture code used when no configured culture matches.</param>
    /// <param name="definitions">Cultures available to the application.</param>
    public SupportedCultureCatalog(string defaultCultureCode, IEnumerable<SupportedCultureDefinition> definitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultCultureCode);
        ArgumentNullException.ThrowIfNull(definitions);

        string canonicalDefaultCultureCode = Canonicalize(defaultCultureCode);
        SupportedCultureDefinition[] orderedDefinitions = definitions
            .OrderBy(definition => definition.SortOrder)
            .ThenBy(definition => definition.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (orderedDefinitions.Length == 0)
        {
            throw new ArgumentException("At least one supported culture is required.", nameof(definitions));
        }

        string? duplicateCode = orderedDefinitions
            .GroupBy(definition => definition.Code, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;

        if (duplicateCode is not null)
        {
            throw new ArgumentException($"Duplicate supported culture code '{duplicateCode}'.", nameof(definitions));
        }

        if (!orderedDefinitions.Any(definition => string.Equals(definition.Code, canonicalDefaultCultureCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("The default culture must be included in the supported culture definitions.", nameof(defaultCultureCode));
        }

        DefaultCultureCode = canonicalDefaultCultureCode;
        this.definitions = orderedDefinitions;
    }

    /// <summary>
    /// Gets the default UI culture code used when no configured culture is selected.
    /// </summary>
    public string DefaultCultureCode { get; }

    /// <summary>
    /// Creates language selection options using the provided localized display name resolver.
    /// </summary>
    /// <param name="currentCulture">Currently active UI culture.</param>
    /// <param name="getString">Function that resolves display name resource keys.</param>
    /// <returns>Supported cultures sorted for display with the current culture marked as selected.</returns>
    public IReadOnlyList<SupportedCultureOption> CreateOptions(
        CultureInfo currentCulture,
        Func<string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(currentCulture);
        ArgumentNullException.ThrowIfNull(getString);

        string selectedCode = NormalizeForComparison(currentCulture.Name);
        return definitions
            .Select(definition => new SupportedCultureOption(
                definition.Code,
                getString(definition.ResourceKey),
                NormalizeForComparison(definition.Code).Equals(selectedCode, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    /// <summary>
    /// Returns a canonical supported culture code, or the default culture when the input is unsupported.
    /// </summary>
    /// <param name="cultureCode">Culture code to validate.</param>
    /// <returns>A canonical supported culture code.</returns>
    public string ValidateOrDefault(string? cultureCode)
    {
        string? supportedCultureCode = TryGetSupportedCultureCode(cultureCode);
        return supportedCultureCode ?? DefaultCultureCode;
    }

    /// <summary>
    /// Selects the first supported culture that matches a preferred culture list.
    /// </summary>
    /// <param name="preferredCultureCodes">Preferred culture codes in priority order.</param>
    /// <returns>The best supported culture code, or the default culture when no preference matches.</returns>
    public string MatchPreferredCulture(IEnumerable<string?> preferredCultureCodes)
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

    private string? TryGetSupportedCultureCode(string? cultureCode)
    {
        if (string.IsNullOrWhiteSpace(cultureCode))
        {
            return null;
        }

        try
        {
            string canonicalCode = Canonicalize(cultureCode);
            return definitions
                .Select(definition => definition.Code)
                .FirstOrDefault(code => string.Equals(code, canonicalCode, StringComparison.OrdinalIgnoreCase));
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private string? TryGetSupportedLanguageFamilyCode(string? cultureCode)
    {
        if (string.IsNullOrWhiteSpace(cultureCode))
        {
            return null;
        }

        try
        {
            string languageFamily = CultureInfo.GetCultureInfo(cultureCode.Trim().Replace('_', '-')).TwoLetterISOLanguageName;
            return definitions
                .Select(definition => definition.Code)
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
}
