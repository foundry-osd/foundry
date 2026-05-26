using System.Globalization;

namespace Foundry.Localization;

/// <summary>
/// Provides validation and display metadata for a configured set of UI cultures.
/// </summary>
public sealed class SupportedCultureCatalog
{
    private static readonly HashSet<string> LatinAmericanSpanishRegions = new(StringComparer.OrdinalIgnoreCase)
    {
        "AR",
        "BO",
        "CL",
        "CO",
        "CR",
        "CU",
        "DO",
        "EC",
        "GT",
        "HN",
        "MX",
        "NI",
        "PA",
        "PE",
        "PR",
        "PY",
        "SV",
        "US",
        "UY",
        "VE",
        "419"
    };

    private static readonly IReadOnlyDictionary<string, string> LanguageFamilyFallbacks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ar"] = "ar-SA",
        ["bg"] = "bg-BG",
        ["cs"] = "cs-CZ",
        ["da"] = "da-DK",
        ["de"] = "de-DE",
        ["el"] = "el-GR",
        ["en"] = "en-US",
        ["es"] = "es-ES",
        ["et"] = "et-EE",
        ["fi"] = "fi-FI",
        ["fr"] = "fr-FR",
        ["he"] = "he-IL",
        ["hr"] = "hr-HR",
        ["hu"] = "hu-HU",
        ["it"] = "it-IT",
        ["ja"] = "ja-JP",
        ["ko"] = "ko-KR",
        ["lt"] = "lt-LT",
        ["lv"] = "lv-LV",
        ["nb"] = "nb-NO",
        ["nl"] = "nl-NL",
        ["pl"] = "pl-PL",
        ["pt"] = "pt-PT",
        ["ro"] = "ro-RO",
        ["ru"] = "ru-RU",
        ["sk"] = "sk-SK",
        ["sl"] = "sl-SI",
        ["sr"] = "sr-Latn-RS",
        ["sv"] = "sv-SE",
        ["th"] = "th-TH",
        ["tr"] = "tr-TR",
        ["uk"] = "uk-UA",
        ["zh"] = "zh-CN"
    };

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

            string? fallbackMatch = TryGetSupportedFallbackCultureCode(cultureCode);
            if (fallbackMatch is not null)
            {
                return fallbackMatch;
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

    private string? TryGetSupportedFallbackCultureCode(string? cultureCode)
    {
        if (string.IsNullOrWhiteSpace(cultureCode))
        {
            return null;
        }

        string normalizedCode = cultureCode.Trim().Replace('_', '-');
        string? regionalFallback = TryGetRegionalFallbackCode(normalizedCode);
        if (regionalFallback is not null)
        {
            string? supportedRegionalFallback = TryGetSupportedCultureCode(regionalFallback);
            if (supportedRegionalFallback is not null)
            {
                return supportedRegionalFallback;
            }
        }

        string? languageFamily = TryGetLanguageFamily(normalizedCode);
        if (languageFamily is not null
            && LanguageFamilyFallbacks.TryGetValue(languageFamily, out string? fallbackCode))
        {
            return TryGetSupportedCultureCode(fallbackCode);
        }

        return null;
    }

    private static string? TryGetRegionalFallbackCode(string normalizedCode)
    {
        string lowerCode = normalizedCode.ToLowerInvariant();
        string[] parts = normalizedCode.Split('-', StringSplitOptions.RemoveEmptyEntries);
        string language = parts.Length > 0 ? parts[0] : string.Empty;
        string? region = parts.Length > 1 ? parts[^1] : null;

        if (lowerCode.Equals("no", StringComparison.OrdinalIgnoreCase)
            || lowerCode.StartsWith("no-", StringComparison.OrdinalIgnoreCase)
            || lowerCode.StartsWith("nn-", StringComparison.OrdinalIgnoreCase))
        {
            return "nb-NO";
        }

        if (lowerCode.Equals("zh-hant", StringComparison.OrdinalIgnoreCase)
            || lowerCode.StartsWith("zh-hant-", StringComparison.OrdinalIgnoreCase)
            || lowerCode.Equals("zh-tw", StringComparison.OrdinalIgnoreCase)
            || lowerCode.Equals("zh-hk", StringComparison.OrdinalIgnoreCase)
            || lowerCode.Equals("zh-mo", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-TW";
        }

        if (lowerCode.Equals("zh-hans", StringComparison.OrdinalIgnoreCase)
            || lowerCode.StartsWith("zh-hans-", StringComparison.OrdinalIgnoreCase)
            || lowerCode.Equals("zh-cn", StringComparison.OrdinalIgnoreCase)
            || lowerCode.Equals("zh-sg", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-CN";
        }

        if (lowerCode.Equals("sr", StringComparison.OrdinalIgnoreCase)
            || lowerCode.StartsWith("sr-", StringComparison.OrdinalIgnoreCase))
        {
            return "sr-Latn-RS";
        }

        if (language.Equals("es", StringComparison.OrdinalIgnoreCase)
            && region is not null
            && LatinAmericanSpanishRegions.Contains(region))
        {
            return "es-MX";
        }

        return null;
    }

    private static string? TryGetLanguageFamily(string normalizedCode)
    {
        try
        {
            return CultureInfo.GetCultureInfo(normalizedCode).TwoLetterISOLanguageName;
        }
        catch (CultureNotFoundException)
        {
            string language = normalizedCode.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            return string.IsNullOrWhiteSpace(language) ? null : language;
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
