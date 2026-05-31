using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Catalog;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.ViewModels;

public sealed partial class OperatingSystemCatalogViewModel : ObservableObject
{
    private const string DefaultReleaseId = OperatingSystemSupportMatrix.DefaultReleaseId;
    private const string DefaultLicenseChannel = OperatingSystemSupportMatrix.DefaultLicenseChannel;
    private const string DefaultEdition = OperatingSystemSupportMatrix.DefaultEdition;
    private const string FallbackLanguageCode = "en-US";
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
    private readonly HashSet<string> _configuredAllowedLanguageCodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _configuredAllowedReleaseIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _configuredAllowedLicenseChannels = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _configuredAllowedEditions = new(StringComparer.OrdinalIgnoreCase);
    private string? _configuredDefaultLanguageCode;
    private string? _configuredDefaultReleaseId;
    private string? _configuredDefaultLicenseChannel;
    private string? _configuredDefaultEdition;
    private bool _isReleaseIdRestrictedToSingleOption;
    private bool _isLanguageRestrictedToSingleOption;
    private bool _isLicenseChannelRestrictedToSingleOption;
    private bool _isEditionRestrictedToSingleOption;
    private bool _hasLoggedUnavailableConfiguredLanguages;
    private bool _hasLoggedUnavailableConfiguredReleaseIds;
    private bool _hasLoggedUnavailableConfiguredLicenseChannels;
    private bool _hasLoggedUnavailableConfiguredEditions;
    private bool _hasLoggedUnavailableDefaultLanguage;
    private bool _hasLoggedUnavailableDefaultReleaseId;
    private bool _hasLoggedUnavailableDefaultLicenseChannel;
    private bool _hasLoggedUnavailableDefaultEdition;
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

    public bool IsReleaseIdSelectionEnabled => !_isReleaseIdRestrictedToSingleOption;

    public bool IsLanguageSelectionEnabled => !_isLanguageRestrictedToSingleOption;

    public bool IsLicenseChannelSelectionEnabled => !_isLicenseChannelRestrictedToSingleOption;

    public bool IsEditionSelectionEnabled => !_isEditionRestrictedToSingleOption;

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

    public void ApplyOperatingSystemSelection(DeployOperatingSystemSelectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        ResetOperatingSystemSelectionPolicy(settings);

        RefreshFilterOptions();
        ApplySelection();
        OnPropertyChanged(nameof(IsReleaseIdSelectionEnabled));
        OnPropertyChanged(nameof(IsLanguageSelectionEnabled));
        OnPropertyChanged(nameof(IsLicenseChannelSelectionEnabled));
        OnPropertyChanged(nameof(IsEditionSelectionEnabled));
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
            IEnumerable<string> effectiveReleaseValues = BuildEffectiveFilterValues(
                releaseScope.Select(item => item.ReleaseId),
                _configuredAllowedReleaseIds,
                "release IDs",
                ref _hasLoggedUnavailableConfiguredReleaseIds,
                out _isReleaseIdRestrictedToSingleOption);
            SelectedReleaseId = UpdateFilterSelection(
                ReleaseIdFilters,
                effectiveReleaseValues,
                previousReleaseId,
                _configuredDefaultReleaseId ?? DefaultReleaseId,
                selectFirstWhenNoMatch: true);
            LogUnavailableDefault(
                _configuredDefaultReleaseId,
                ReleaseIdFilters,
                "release ID",
                ref _hasLoggedUnavailableDefaultReleaseId);

            IEnumerable<OperatingSystemCatalogItem> languageScope = ApplyReleaseIdFilter(releaseScope);
            IEnumerable<string> effectiveLanguageValues = BuildEffectiveLanguageFilterValues(
                languageScope,
                out _isLanguageRestrictedToSingleOption);
            SelectedLanguageCode = UpdateLanguageFilterSelection(
                LanguageFilters,
                effectiveLanguageValues,
                previousLanguageCode,
                _configuredDefaultLanguageCode);
            LogUnavailableDefaultLanguage();

            IEnumerable<OperatingSystemCatalogItem> licenseScope = ApplyLanguageFilter(languageScope);
            IEnumerable<string> effectiveLicenseChannelValues = BuildEffectiveFilterValues(
                licenseScope.Select(item => item.LicenseChannel),
                _configuredAllowedLicenseChannels,
                "license channels",
                ref _hasLoggedUnavailableConfiguredLicenseChannels,
                out _isLicenseChannelRestrictedToSingleOption);
            SelectedLicenseChannel = UpdateFilterSelection(
                LicenseChannelFilters,
                effectiveLicenseChannelValues,
                previousLicenseChannel,
                _configuredDefaultLicenseChannel ?? DefaultLicenseChannel,
                selectFirstWhenNoMatch: true);
            LogUnavailableDefault(
                _configuredDefaultLicenseChannel,
                LicenseChannelFilters,
                "license channel",
                ref _hasLoggedUnavailableDefaultLicenseChannel);

            IEnumerable<OperatingSystemCatalogItem> editionScope = ApplyLicenseChannelFilter(licenseScope);
            IEnumerable<string> recommendedEditions = BuildRecommendedEditionOptions(editionScope);
            IEnumerable<string> effectiveEditionValues = BuildEffectiveFilterValues(
                recommendedEditions,
                _configuredAllowedEditions,
                "editions",
                ref _hasLoggedUnavailableConfiguredEditions,
                out _isEditionRestrictedToSingleOption);
            SelectedEdition = UpdateFilterSelection(
                EditionFilters,
                effectiveEditionValues,
                previousEdition,
                _configuredDefaultEdition ?? DefaultEdition,
                selectFirstWhenNoMatch: true);
            LogUnavailableDefault(
                _configuredDefaultEdition,
                EditionFilters,
                "edition",
                ref _hasLoggedUnavailableDefaultEdition);
        }
        finally
        {
            _isUpdatingFilters = false;
            OnPropertyChanged(nameof(IsReleaseIdSelectionEnabled));
            OnPropertyChanged(nameof(IsLanguageSelectionEnabled));
            OnPropertyChanged(nameof(IsLicenseChannelSelectionEnabled));
            OnPropertyChanged(nameof(IsEditionSelectionEnabled));
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

    private IEnumerable<string> BuildEffectiveLanguageFilterValues(
        IEnumerable<OperatingSystemCatalogItem> source,
        out bool isRestrictedToSingleOption)
    {
        string[] availableLanguageValues = source
            .Select(GetLanguageFilterValue)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (_configuredAllowedLanguageCodes.Count == 0)
        {
            isRestrictedToSingleOption = false;
            return availableLanguageValues;
        }

        if (availableLanguageValues.Length == 0)
        {
            isRestrictedToSingleOption = false;
            return availableLanguageValues;
        }

        string[] filteredLanguageValues = availableLanguageValues
            .Where(value => _configuredAllowedLanguageCodes.Contains(LanguageCodeUtility.NormalizeForComparison(value)))
            .ToArray();

        if (filteredLanguageValues.Length > 0)
        {
            isRestrictedToSingleOption = filteredLanguageValues.Length == 1;
            return filteredLanguageValues;
        }

        if (!_hasLoggedUnavailableConfiguredLanguages)
        {
            string configuredLanguages = string.Join(", ", _configuredAllowedLanguageCodes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
            _logger.LogWarning(
                "Configured visible languages [{ConfiguredLanguages}] do not match the current catalog scope. Falling back to the catalog languages.",
                configuredLanguages);
            _hasLoggedUnavailableConfiguredLanguages = true;
        }

        isRestrictedToSingleOption = false;
        return availableLanguageValues;
    }

    private IEnumerable<string> BuildEffectiveFilterValues(
        IEnumerable<string> availableValues,
        HashSet<string> configuredAllowedValues,
        string selectorName,
        ref bool hasLoggedUnavailableValues,
        out bool isRestrictedToSingleOption)
    {
        string[] available = availableValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (configuredAllowedValues.Count == 0 || available.Length == 0)
        {
            isRestrictedToSingleOption = false;
            return available;
        }

        string[] filtered = available
            .Where(configuredAllowedValues.Contains)
            .ToArray();

        if (filtered.Length > 0)
        {
            isRestrictedToSingleOption = filtered.Length == 1;
            return filtered;
        }

        if (!hasLoggedUnavailableValues)
        {
            string configuredValues = string.Join(", ", configuredAllowedValues.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
            _logger.LogWarning(
                "Configured operating system {SelectorName} [{ConfiguredValues}] do not match the current catalog scope. Falling back to the catalog values.",
                selectorName,
                configuredValues);
            hasLoggedUnavailableValues = true;
        }

        isRestrictedToSingleOption = false;
        return available;
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

    private void ResetOperatingSystemSelectionPolicy(DeployOperatingSystemSelectionSettings settings)
    {
        if (!settings.IsEnabled)
        {
            ResetConfiguredValues(_configuredAllowedLanguageCodes, [], NormalizeLanguageCode);
            ResetConfiguredValues(_configuredAllowedReleaseIds, [], static value => value.Trim());
            ResetConfiguredValues(_configuredAllowedLicenseChannels, [], NormalizeLicenseChannel);
            ResetConfiguredValues(_configuredAllowedEditions, [], static value => value.Trim());
            _configuredDefaultLanguageCode = null;
            _configuredDefaultReleaseId = null;
            _configuredDefaultLicenseChannel = null;
            _configuredDefaultEdition = null;
            ResetOperatingSystemSelectionLogState();
            return;
        }

        ResetConfiguredValues(_configuredAllowedLanguageCodes, settings.AllowedLanguageCodes, NormalizeLanguageCode);
        ResetConfiguredValues(_configuredAllowedReleaseIds, settings.AllowedReleaseIds, static value => value.Trim());
        ResetConfiguredValues(_configuredAllowedLicenseChannels, settings.AllowedLicenseChannels, NormalizeLicenseChannel);
        ResetConfiguredValues(_configuredAllowedEditions, settings.AllowedEditions, static value => value.Trim());

        _configuredDefaultLanguageCode = NormalizeOptionalLanguageCode(settings.DefaultLanguageCode);
        _configuredDefaultReleaseId = NormalizeOptionalKnownValue(settings.DefaultReleaseId, OperatingSystemSupportMatrix.ReleaseSearchOrder, static value => value.Trim());
        _configuredDefaultLicenseChannel = NormalizeOptionalKnownValue(settings.DefaultLicenseChannel, OperatingSystemSupportMatrix.LicenseChannelOrder, NormalizeLicenseChannel);
        _configuredDefaultEdition = NormalizeOptionalKnownValue(settings.DefaultEdition, OperatingSystemSupportMatrix.EditionOrder, static value => value.Trim());

        ResetOperatingSystemSelectionLogState();
    }

    private void ResetOperatingSystemSelectionLogState()
    {
        _hasLoggedUnavailableConfiguredLanguages = false;
        _hasLoggedUnavailableConfiguredReleaseIds = false;
        _hasLoggedUnavailableConfiguredLicenseChannels = false;
        _hasLoggedUnavailableConfiguredEditions = false;
        _hasLoggedUnavailableDefaultLanguage = false;
        _hasLoggedUnavailableDefaultReleaseId = false;
        _hasLoggedUnavailableDefaultLicenseChannel = false;
        _hasLoggedUnavailableDefaultEdition = false;
    }

    private static void ResetConfiguredValues(
        HashSet<string> target,
        IEnumerable<string> values,
        Func<string, string> normalize)
    {
        target.Clear();
        foreach (string value in values)
        {
            string normalized = normalize(value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                target.Add(normalized);
            }
        }
    }

    private static string? NormalizeOptionalKnownValue(
        string? value,
        IEnumerable<string> supportedValues,
        Func<string, string> normalize)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = normalize(value);
        return supportedValues.FirstOrDefault(supported =>
            string.Equals(supported, normalized, StringComparison.OrdinalIgnoreCase));
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
        string previousSelection,
        string? defaultSelection)
    {
        UpdateFilterCollection(target, values);
        if (target.Count == 0)
        {
            return string.Empty;
        }

        foreach (string? candidate in new[] { previousSelection, defaultSelection, DefaultLanguageCode, FallbackLanguageCode })
        {
            string selected = EnsureLanguageSelection(candidate ?? string.Empty, target);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                return selected;
            }
        }

        return target[0];
    }

    private void LogUnavailableDefaultLanguage()
    {
        if (_hasLoggedUnavailableDefaultLanguage ||
            string.IsNullOrWhiteSpace(_configuredDefaultLanguageCode) ||
            LanguageFilters.Count == 0 ||
            !string.IsNullOrWhiteSpace(EnsureLanguageSelection(_configuredDefaultLanguageCode, LanguageFilters)))
        {
            return;
        }

        _logger.LogWarning(
            "Configured default language '{LanguageCode}' is not available in the current catalog scope and was ignored.",
            _configuredDefaultLanguageCode);
        _hasLoggedUnavailableDefaultLanguage = true;
    }

    private void LogUnavailableDefault(
        string? configuredDefault,
        ObservableCollection<string> options,
        string selectorName,
        ref bool hasLoggedUnavailableDefault)
    {
        if (hasLoggedUnavailableDefault ||
            string.IsNullOrWhiteSpace(configuredDefault) ||
            options.Count == 0 ||
            options.Any(option => option.Equals(configuredDefault, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _logger.LogWarning(
            "Configured default operating system {SelectorName} '{ConfiguredDefault}' is not available in the current catalog scope and was ignored.",
            selectorName,
            configuredDefault);
        hasLoggedUnavailableDefault = true;
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
            ? LanguageCodeUtility.Canonicalize(item.LanguageCode)
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
            string canonical = LanguageCodeUtility.Canonicalize(candidate);
            if (!string.IsNullOrWhiteSpace(canonical))
            {
                return canonical;
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

        string normalized = LanguageCodeUtility.NormalizeForComparison(languageCode);
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : LanguageCodeUtility.Canonicalize(languageCode);
    }

    private static string NormalizeLanguageCode(string languageCode)
    {
        return LanguageCodeUtility.NormalizeForComparison(languageCode);
    }

    private static string NormalizeLicenseChannel(string? value)
    {
        string normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        return normalized switch
        {
            "RETAIL" => "RET",
            "VOLUME" => "VOL",
            _ => normalized
        };
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
