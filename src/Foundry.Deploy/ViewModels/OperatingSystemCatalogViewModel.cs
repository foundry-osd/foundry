using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Deploy.Models;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.ViewModels;

public sealed partial class OperatingSystemCatalogViewModel : ObservableObject
{
    private const string DefaultReleaseId = OperatingSystemSupportMatrix.DefaultReleaseId;
    private const string DefaultLicenseChannel = "RET";
    private const string DefaultEdition = "Pro";
    private const string FallbackLanguageCode = "en-us";
    private static readonly string DefaultLanguageCode = ResolveDefaultLanguageCode();
    private static readonly string[] RetailEditionOptions =
    [
        "Home",
        "Home N",
        "Home Single Language",
        "Education",
        "Education N",
        "Pro",
        "Pro N",
        "Enterprise",
        "Enterprise N"
    ];
    private static readonly string[] VolumeEditionOptions =
    [
        "Education",
        "Education N",
        "Pro",
        "Pro N",
        "Enterprise",
        "Enterprise N"
    ];
    private static readonly string[] Arm64RetailEditionOptions =
    [
        "Home",
        "Pro",
        "Enterprise"
    ];
    private static readonly string[] Arm64VolumeEditionOptions =
    [
        "Pro",
        "Enterprise"
    ];

    private readonly ILogger _logger;
    private readonly HashSet<string> _configuredVisibleLanguageCodes = new(StringComparer.OrdinalIgnoreCase);
    private string? _configuredDefaultLanguageCodeOverride;
    private bool _forceSingleVisibleLanguageSelection;
    private bool _hasLoggedUnavailableConfiguredLanguages;
    private bool _hasLoggedUnavailableDefaultLanguageOverride;
    private bool _isUpdatingFilters;

    public OperatingSystemCatalogViewModel(ILogger logger, string initialArchitecture)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        effectiveOsArchitecture = NormalizeArchitecture(initialArchitecture);
    }

    public event EventHandler? StateChanged;

    [ObservableProperty]
    private OperatingSystemCatalogItem? selectedOperatingSystem;

    [ObservableProperty]
    private string effectiveOsArchitecture;

    [ObservableProperty]
    private string selectedReleaseId = DefaultReleaseId;

    [ObservableProperty]
    private string selectedLanguageCode = DefaultLanguageCode;

    [ObservableProperty]
    private string selectedLicenseChannel = DefaultLicenseChannel;

    [ObservableProperty]
    private string selectedEdition = DefaultEdition;

    public ObservableCollection<OperatingSystemCatalogItem> OperatingSystems { get; } = [];

    public ObservableCollection<string> ReleaseIdFilters { get; } = [];

    public ObservableCollection<string> LanguageFilters { get; } = [];

    public ObservableCollection<string> LicenseChannelFilters { get; } = [];

    public ObservableCollection<string> EditionFilters { get; } = [];

    public bool IsLanguageSelectionEnabled => !(_forceSingleVisibleLanguageSelection && LanguageFilters.Count == 1);

    public void ApplyCatalog(IReadOnlyList<OperatingSystemCatalogItem> operatingSystems)
    {
        ArgumentNullException.ThrowIfNull(operatingSystems);

        OperatingSystems.Clear();
        foreach (OperatingSystemCatalogItem item in operatingSystems)
        {
            OperatingSystems.Add(item);
        }

        RefreshFilterOptions();
        ApplySelection();
        RaiseStateChanged();
    }

    public void ApplyExpertLocalization(IEnumerable<string> visibleLanguageCodes, string? defaultLanguageCodeOverride, bool forceSingleVisibleLanguageSelection)
    {
        ArgumentNullException.ThrowIfNull(visibleLanguageCodes);

        _configuredVisibleLanguageCodes.Clear();
        foreach (string languageCode in visibleLanguageCodes)
        {
            string normalized = NormalizeLanguageCode(languageCode);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                _configuredVisibleLanguageCodes.Add(normalized);
            }
        }

        _configuredDefaultLanguageCodeOverride = NormalizeOptionalLanguageCode(defaultLanguageCodeOverride);
        _forceSingleVisibleLanguageSelection = forceSingleVisibleLanguageSelection;
        _hasLoggedUnavailableConfiguredLanguages = false;
        _hasLoggedUnavailableDefaultLanguageOverride = false;

        RefreshFilterOptions();
        ApplySelection();
        OnPropertyChanged(nameof(IsLanguageSelectionEnabled));
        RaiseStateChanged();
    }

    public void SetEffectiveArchitecture(string architecture)
    {
        string normalized = NormalizeArchitecture(architecture);
        if (string.Equals(EffectiveOsArchitecture, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        EffectiveOsArchitecture = normalized;
    }

    public bool IsReadyForNavigation()
    {
        return OperatingSystems.Count > 0 &&
               ReleaseIdFilters.Count > 0 &&
               LanguageFilters.Count > 0 &&
               LicenseChannelFilters.Count > 0 &&
               EditionFilters.Count > 0 &&
               SelectedOperatingSystem is not null;
    }

    partial void OnSelectedOperatingSystemChanged(OperatingSystemCatalogItem? value)
    {
        RaiseStateChanged();
    }

    partial void OnEffectiveOsArchitectureChanged(string value)
    {
        if (_isUpdatingFilters)
        {
            return;
        }

        RefreshFilterOptions();
        ApplySelection();
        RaiseStateChanged();
    }

    partial void OnSelectedReleaseIdChanged(string value)
    {
        HandleFilterSelectionChanged();
    }

    partial void OnSelectedLanguageCodeChanged(string value)
    {
        HandleFilterSelectionChanged();
    }

    partial void OnSelectedLicenseChannelChanged(string value)
    {
        HandleFilterSelectionChanged();
    }

    partial void OnSelectedEditionChanged(string value)
    {
        HandleFilterSelectionChanged();
    }

    private void HandleFilterSelectionChanged()
    {
        if (_isUpdatingFilters)
        {
            return;
        }

        RefreshFilterOptions();
        ApplySelection();
        RaiseStateChanged();
    }

    private void ApplySelection()
    {
        OperatingSystemCatalogItem[] filtered = BuildFilteredOperatingSystems();

        OperatingSystemCatalogItem? matchingCurrent = SelectedOperatingSystem is null
            ? null
            : filtered.FirstOrDefault(item => IsSameOperatingSystemMedia(item, SelectedOperatingSystem));

        OperatingSystemCatalogItem? selected = matchingCurrent ?? filtered.FirstOrDefault();
        if (selected is null)
        {
            SelectedOperatingSystem = null;
            return;
        }

        SelectedOperatingSystem = ApplyEditionSelection(selected);
    }

    private void RefreshFilterOptions()
    {
        _isUpdatingFilters = true;

        try
        {
            string previousReleaseId = SelectedReleaseId;
            string previousLanguageCode = SelectedLanguageCode;
            string previousLicenseChannel = SelectedLicenseChannel;
            string previousEdition = SelectedEdition;

            IEnumerable<OperatingSystemCatalogItem> baseQuery = BuildOsQueryWithArchitecture(OperatingSystems);

            IEnumerable<OperatingSystemCatalogItem> releaseScope = baseQuery;
            SelectedReleaseId = UpdateFilterSelection(
                ReleaseIdFilters,
                releaseScope.Select(item => item.ReleaseId),
                previousReleaseId,
                DefaultReleaseId,
                selectFirstWhenNoMatch: true);

            IEnumerable<OperatingSystemCatalogItem> languageScope = ApplyReleaseIdFilter(releaseScope);
            IEnumerable<string> effectiveLanguageValues = BuildEffectiveLanguageFilterValues(languageScope);
            SelectedLanguageCode = UpdateLanguageFilterSelection(
                LanguageFilters,
                effectiveLanguageValues,
                previousLanguageCode);

            string configuredDefaultLanguageCode = EnsureLanguageSelection(
                _configuredDefaultLanguageCodeOverride ?? string.Empty,
                LanguageFilters);
            if (!string.IsNullOrWhiteSpace(configuredDefaultLanguageCode))
            {
                SelectedLanguageCode = configuredDefaultLanguageCode;
            }
            else if (!_hasLoggedUnavailableDefaultLanguageOverride &&
                     !string.IsNullOrWhiteSpace(_configuredDefaultLanguageCodeOverride))
            {
                _logger.LogWarning(
                    "Configured default language override '{LanguageCode}' is not available in the current catalog scope and was ignored.",
                    _configuredDefaultLanguageCodeOverride);
                _hasLoggedUnavailableDefaultLanguageOverride = true;
            }

            IEnumerable<OperatingSystemCatalogItem> licenseScope = ApplyLanguageFilter(languageScope);
            SelectedLicenseChannel = UpdateFilterSelection(
                LicenseChannelFilters,
                licenseScope.Select(item => item.LicenseChannel),
                previousLicenseChannel,
                DefaultLicenseChannel,
                selectFirstWhenNoMatch: true);

            IEnumerable<OperatingSystemCatalogItem> editionScope = ApplyLicenseChannelFilter(licenseScope);
            IEnumerable<string> recommendedEditions = BuildRecommendedEditionOptions(editionScope);
            SelectedEdition = UpdateFilterSelection(
                EditionFilters,
                recommendedEditions,
                previousEdition,
                DefaultEdition,
                selectFirstWhenNoMatch: true);
        }
        finally
        {
            _isUpdatingFilters = false;
            OnPropertyChanged(nameof(IsLanguageSelectionEnabled));
        }
    }

    private IEnumerable<OperatingSystemCatalogItem> BuildOsQueryWithArchitecture(IEnumerable<OperatingSystemCatalogItem> source)
    {
        IEnumerable<OperatingSystemCatalogItem> supportedSource = source.Where(OperatingSystemSupportMatrix.IsSupported);
        string architecture = NormalizeArchitecture(EffectiveOsArchitecture);
        if (string.IsNullOrWhiteSpace(architecture))
        {
            return supportedSource;
        }

        return supportedSource.Where(item => IsArchitectureMatch(item.Architecture, architecture));
    }

    private IEnumerable<OperatingSystemCatalogItem> ApplyReleaseIdFilter(IEnumerable<OperatingSystemCatalogItem> source)
    {
        return IsFilterUnset(SelectedReleaseId)
            ? source
            : source.Where(item => item.ReleaseId.Equals(SelectedReleaseId, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<OperatingSystemCatalogItem> ApplyLanguageFilter(IEnumerable<OperatingSystemCatalogItem> source)
    {
        return IsFilterUnset(SelectedLanguageCode)
            ? source
            : source.Where(item => GetLanguageFilterValue(item).Equals(SelectedLanguageCode, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<string> BuildEffectiveLanguageFilterValues(IEnumerable<OperatingSystemCatalogItem> source)
    {
        string[] availableLanguageValues = source
            .Select(GetLanguageFilterValue)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (_configuredVisibleLanguageCodes.Count == 0)
        {
            return availableLanguageValues;
        }

        string[] filteredLanguageValues = availableLanguageValues
            .Where(value => _configuredVisibleLanguageCodes.Contains(NormalizeLanguageCode(value)))
            .ToArray();

        if (filteredLanguageValues.Length > 0)
        {
            return filteredLanguageValues;
        }

        if (!_hasLoggedUnavailableConfiguredLanguages)
        {
            string configuredLanguages = string.Join(", ", _configuredVisibleLanguageCodes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
            _logger.LogWarning(
                "Configured visible languages [{ConfiguredLanguages}] do not match the current catalog scope. Falling back to the catalog languages.",
                configuredLanguages);
            _hasLoggedUnavailableConfiguredLanguages = true;
        }

        return availableLanguageValues;
    }

    private IEnumerable<OperatingSystemCatalogItem> ApplyLicenseChannelFilter(IEnumerable<OperatingSystemCatalogItem> source)
    {
        return IsFilterUnset(SelectedLicenseChannel)
            ? source
            : source.Where(item => item.LicenseChannel.Equals(SelectedLicenseChannel, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<string> BuildRecommendedEditionOptions(IEnumerable<OperatingSystemCatalogItem> scope)
    {
        string architecture = NormalizeArchitecture(EffectiveOsArchitecture);
        bool hasRetail = scope.Any(item => item.LicenseChannel.Equals("RET", StringComparison.OrdinalIgnoreCase));
        bool hasVolume = scope.Any(item => item.LicenseChannel.Equals("VOL", StringComparison.OrdinalIgnoreCase));

        List<string> recommended = [];

        if (IsFilterUnset(SelectedLicenseChannel))
        {
            if (hasRetail)
            {
                recommended.AddRange(GetEditionOptionsForChannel("RET", architecture));
            }

            if (hasVolume)
            {
                recommended.AddRange(GetEditionOptionsForChannel("VOL", architecture));
            }
        }
        else
        {
            recommended.AddRange(GetEditionOptionsForChannel(SelectedLicenseChannel, architecture));
        }

        if (recommended.Count == 0)
        {
            recommended.AddRange(scope.Select(item => item.Edition));
        }

        return recommended;
    }

    private OperatingSystemCatalogItem[] BuildFilteredOperatingSystems()
    {
        IEnumerable<OperatingSystemCatalogItem> query = BuildOsQueryWithArchitecture(OperatingSystems);
        query = ApplyReleaseIdFilter(query);
        query = ApplyLanguageFilter(query);
        query = ApplyLicenseChannelFilter(query);

        return query
            .GroupBy(
                item => string.IsNullOrWhiteSpace(item.Url) ? item.FileName : item.Url,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private OperatingSystemCatalogItem ApplyEditionSelection(OperatingSystemCatalogItem item)
    {
        if (IsFilterUnset(SelectedEdition))
        {
            return item;
        }

        if (item.Edition.Equals(SelectedEdition, StringComparison.OrdinalIgnoreCase))
        {
            return item;
        }

        return item with { Edition = SelectedEdition };
    }

    private static IEnumerable<string> GetEditionOptionsForChannel(string licenseChannel, string architecture)
    {
        bool isArm64 = architecture.Equals("arm64", StringComparison.OrdinalIgnoreCase);

        if (licenseChannel.Equals("VOL", StringComparison.OrdinalIgnoreCase))
        {
            return isArm64 ? Arm64VolumeEditionOptions : VolumeEditionOptions;
        }

        if (licenseChannel.Equals("RET", StringComparison.OrdinalIgnoreCase))
        {
            return isArm64 ? Arm64RetailEditionOptions : RetailEditionOptions;
        }

        return [];
    }

    private static void UpdateFilterCollection(ObservableCollection<string> target, IEnumerable<string> values)
    {
        string[] normalizedValues = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        target.Clear();

        foreach (string value in normalizedValues)
        {
            target.Add(value);
        }
    }

    private static bool TryGetFilterSelection(string? selectedValue, ObservableCollection<string> options, out string matched)
    {
        matched = string.Empty;

        if (options.Count == 0 || IsFilterUnset(selectedValue ?? string.Empty))
        {
            return false;
        }

        string? matchingOption = options.FirstOrDefault(option =>
            option.Equals(selectedValue, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(matchingOption))
        {
            return false;
        }

        matched = matchingOption;
        return true;
    }

    private static string UpdateFilterSelection(
        ObservableCollection<string> target,
        IEnumerable<string> values,
        string previousSelection,
        string? defaultSelection = null,
        bool selectFirstWhenNoMatch = false)
    {
        UpdateFilterCollection(target, values);

        if (target.Count == 0)
        {
            return string.Empty;
        }

        if (TryGetFilterSelection(previousSelection, target, out string selected))
        {
            return selected;
        }

        if (!string.IsNullOrWhiteSpace(defaultSelection) &&
            TryGetFilterSelection(defaultSelection, target, out selected))
        {
            return selected;
        }

        return selectFirstWhenNoMatch ? target[0] : string.Empty;
    }

    private static string UpdateLanguageFilterSelection(
        ObservableCollection<string> target,
        IEnumerable<string> values,
        string previousSelection)
    {
        UpdateFilterCollection(target, values);
        if (target.Count == 0)
        {
            return string.Empty;
        }

        foreach (string candidate in new[] { previousSelection, DefaultLanguageCode, FallbackLanguageCode })
        {
            string selected = EnsureLanguageSelection(candidate, target);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                return selected;
            }
        }

        return target[0];
    }

    private static string EnsureLanguageSelection(string languageCode, ObservableCollection<string> options)
    {
        if (options.Count == 0)
        {
            return string.Empty;
        }

        if (IsFilterUnset(languageCode))
        {
            return string.Empty;
        }

        string normalized = NormalizeLanguageCode(languageCode);

        string? exact = options.FirstOrDefault(option =>
            NormalizeLanguageCode(option).Equals(normalized, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(exact))
        {
            return exact;
        }

        string neutral = normalized.Split('-', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        if (!string.IsNullOrWhiteSpace(neutral))
        {
            string? sameLanguage = options.FirstOrDefault(option =>
            {
                string candidate = NormalizeLanguageCode(option);
                return candidate.Equals(neutral, StringComparison.OrdinalIgnoreCase) ||
                       candidate.StartsWith($"{neutral}-", StringComparison.OrdinalIgnoreCase);
            });

            if (!string.IsNullOrWhiteSpace(sameLanguage))
            {
                return sameLanguage;
            }
        }

        return string.Empty;
    }

    private static bool IsFilterUnset(string value)
    {
        return string.IsNullOrWhiteSpace(value);
    }

    private static string GetLanguageFilterValue(OperatingSystemCatalogItem item)
    {
        return !string.IsNullOrWhiteSpace(item.LanguageCode)
            ? item.LanguageCode
            : item.Language;
    }

    private static string ResolveDefaultLanguageCode()
    {
        string[] candidates =
        [
            CultureInfo.CurrentUICulture.Name,
            CultureInfo.CurrentCulture.Name,
            CultureInfo.InstalledUICulture.Name
        ];

        foreach (string candidate in candidates)
        {
            string normalized = NormalizeLanguageCode(candidate);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return FallbackLanguageCode;
    }

    private static string? NormalizeOptionalLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return null;
        }

        string normalized = NormalizeLanguageCode(languageCode);
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized;
    }

    private static string NormalizeLanguageCode(string languageCode)
    {
        return languageCode.Trim().Replace('_', '-').ToLowerInvariant();
    }

    private static bool IsArchitectureMatch(string osArchitecture, string driverArchitecture)
    {
        string os = NormalizeArchitecture(osArchitecture);
        string driver = NormalizeArchitecture(driverArchitecture);
        return os.Equals(driver, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOperatingSystemMedia(OperatingSystemCatalogItem left, OperatingSystemCatalogItem right)
    {
        string leftKey = string.IsNullOrWhiteSpace(left.Url) ? left.FileName : left.Url;
        string rightKey = string.IsNullOrWhiteSpace(right.Url) ? right.FileName : right.Url;
        return leftKey.Equals(rightKey, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeArchitecture(string architecture)
    {
        string normalized = architecture.Trim().ToLowerInvariant();
        return normalized switch
        {
            "amd64" => "x64",
            "x64" => "x64",
            "arm64" => "arm64",
            "aarch64" => "arm64",
            _ => normalized
        };
    }

    private void RaiseStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
