// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Utilities.Globalization;

namespace Foundry.ViewModels;

public sealed partial class CustomizationConfigurationViewModel
{
    private const string AutomaticSelectionValue = "";

    private bool isRefreshingOperatingSystemSelectionOptions;

    // English registry names used when a language has no localized Language.* resource.
    private readonly Dictionary<string, string> operatingSystemLanguageFallbackNames = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<SelectableStringOptionViewModel> OperatingSystemLanguageOptions { get; } = [];
    public ObservableCollection<SelectionOption<string>> DefaultOperatingSystemLanguageOptions { get; } = [];
    public ObservableCollection<SelectableStringOptionViewModel> OperatingSystemReleaseOptions { get; } = [];
    public ObservableCollection<SelectionOption<string>> DefaultOperatingSystemReleaseOptions { get; } = [];
    public ObservableCollection<SelectableStringOptionViewModel> OperatingSystemLicenseChannelOptions { get; } = [];
    public ObservableCollection<SelectionOption<string>> DefaultOperatingSystemLicenseChannelOptions { get; } = [];
    public ObservableCollection<SelectableStringOptionViewModel> OperatingSystemEditionOptions { get; } = [];
    public ObservableCollection<SelectionOption<string>> DefaultOperatingSystemEditionOptions { get; } = [];
    public ObservableCollection<SelectionOption<int>> DefaultOperatingSystemMediaOffsetOptions { get; } = [];

    public bool IsOperatingSystemSelectionOptionsEnabled => IsOperatingSystemSelectionEnabled;
    public bool IsDefaultOperatingSystemLanguageSelectionEnabled => IsOperatingSystemSelectionOptionsEnabled && !HasSingleSelectedOption(OperatingSystemLanguageOptions);
    public bool IsDefaultOperatingSystemReleaseSelectionEnabled => IsOperatingSystemSelectionOptionsEnabled && !HasSingleSelectedOption(OperatingSystemReleaseOptions);
    public bool IsDefaultOperatingSystemLicenseChannelSelectionEnabled => IsOperatingSystemSelectionOptionsEnabled && DefaultOperatingSystemLicenseChannelOptions.Count > 1;
    public bool IsDefaultOperatingSystemEditionSelectionEnabled => IsOperatingSystemSelectionOptionsEnabled && !HasSingleSelectedOption(OperatingSystemEditionOptions);

    [ObservableProperty]
    public partial string OperatingSystemVersionGroupHeader { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemVersionGroupDescription { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemLanguageGroupHeader { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemLanguageGroupDescription { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemLicenseChannelGroupHeader { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemLicenseChannelGroupDescription { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemEditionGroupHeader { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemEditionGroupDescription { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemAllowedLanguagesLabel { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemAllowedLanguagesDescription { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemDefaultLanguageLabel { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemDefaultLanguageDescription { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemAllowedReleasesLabel { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemAllowedReleasesDescription { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemDefaultReleaseLabel { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemDefaultReleaseDescription { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemDefaultMediaOffsetLabel { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemDefaultMediaOffsetDescription { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemAllowedLicenseChannelsLabel { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemAllowedLicenseChannelsDescription { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemDefaultLicenseChannelLabel { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemDefaultLicenseChannelDescription { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemAllowedEditionsLabel { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemAllowedEditionsDescription { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemDefaultEditionLabel { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemDefaultEditionDescription { get; set; }

    [ObservableProperty]
    public partial string AutomaticOptionText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOperatingSystemSelectionOptionsEnabled))]
    [NotifyPropertyChangedFor(nameof(IsDefaultOperatingSystemLanguageSelectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsDefaultOperatingSystemReleaseSelectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsDefaultOperatingSystemLicenseChannelSelectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsDefaultOperatingSystemEditionSelectionEnabled))]
    public partial bool IsOperatingSystemSelectionEnabled { get; set; }

    [ObservableProperty]
    public partial SelectionOption<string>? SelectedDefaultOperatingSystemLanguage { get; set; }

    [ObservableProperty]
    public partial SelectionOption<string>? SelectedDefaultOperatingSystemRelease { get; set; }

    [ObservableProperty]
    public partial SelectionOption<string>? SelectedDefaultOperatingSystemLicenseChannel { get; set; }

    [ObservableProperty]
    public partial SelectionOption<string>? SelectedDefaultOperatingSystemEdition { get; set; }

    [ObservableProperty]
    public partial SelectionOption<int>? SelectedDefaultOperatingSystemMediaOffset { get; set; }

    partial void OnSelectedDefaultOperatingSystemLanguageChanged(SelectionOption<string>? value)
    {
        SaveState();
    }

    partial void OnSelectedDefaultOperatingSystemReleaseChanged(SelectionOption<string>? value)
    {
        SaveState();
    }

    partial void OnSelectedDefaultOperatingSystemMediaOffsetChanged(SelectionOption<int>? value)
    {
        SaveState();
    }

    partial void OnSelectedDefaultOperatingSystemLicenseChannelChanged(SelectionOption<string>? value)
    {
        if (isRefreshingOperatingSystemSelectionOptions)
        {
            return;
        }

        SaveState();
    }

    partial void OnSelectedDefaultOperatingSystemEditionChanged(SelectionOption<string>? value)
    {
        if (isRefreshingOperatingSystemSelectionOptions)
        {
            return;
        }

        RefreshOperatingSystemDefaultLicenseChannelOptions(BuildOperatingSystemSelectionSettings());
        SaveState();
    }

    partial void OnIsOperatingSystemSelectionEnabledChanged(bool value)
    {
        RefreshWindowsOptionalFeatureCompatibility();
        SaveState();
    }

    private void InitializeOperatingSystemSelectionOptions(IReadOnlyList<LanguageRegistryEntry> languages)
    {
        Dictionary<string, string> localizedNames = GetLocalizedLanguageNames();
        foreach (LanguageRegistryEntry language in languages
            .OrderBy(language => language.SortOrder)
            .ThenBy(language => language.Code, StringComparer.OrdinalIgnoreCase))
        {
            string code = CultureCode.Canonicalize(language.Code);
            operatingSystemLanguageFallbackNames[code] = language.DisplayName;
            var option = new SelectableStringOptionViewModel(
                code,
                GetOperatingSystemLanguageDisplayName(localizedNames, code),
                language.SortOrder,
                false);
            option.PropertyChanged += OnOperatingSystemSelectionOptionPropertyChanged;
            OperatingSystemLanguageOptions.Add(option);
        }

        AddOperatingSystemSelectionOptions(OperatingSystemReleaseOptions, OperatingSystemSelectionCatalog.SupportedReleaseIds, static value => value);
        AddOperatingSystemSelectionOptions(
            OperatingSystemLicenseChannelOptions,
            OperatingSystemSelectionCatalog.SupportedLicenseChannels,
            FormatLicenseChannel);
        AddOperatingSystemSelectionOptions(OperatingSystemEditionOptions, OperatingSystemSelectionCatalog.SupportedEditions, static value => value);
    }

    private void ApplyOperatingSystemSelectionState(OperatingSystemSelectionSettings settings)
    {
        IsOperatingSystemSelectionEnabled = settings.IsEnabled;
        SetSelectedOptions(OperatingSystemLanguageOptions, settings.AllowedLanguageCodes);
        SetSelectedOptions(OperatingSystemReleaseOptions, settings.AllowedReleaseIds);
        SetSelectedOptions(OperatingSystemLicenseChannelOptions, settings.AllowedLicenseChannels);
        SetSelectedOptions(OperatingSystemEditionOptions, settings.AllowedEditions);
        RefreshOperatingSystemLicenseChannelAvailability();

        RefreshOperatingSystemMediaOffsetOptions(settings.DefaultMediaOffset);
        RefreshOperatingSystemDefaultOptions(settings);
    }

    private OperatingSystemSelectionSettings BuildOperatingSystemSelectionSettings()
    {
        if (!initializedCatalogs.Contains(CustomizationCatalog.OperatingSystemSelection))
        {
            return configurationStateService.Current.OperatingSystemSelection;
        }

        return OperatingSystemSelectionSettingsNormalizer.Normalize(new OperatingSystemSelectionSettings
        {
            IsEnabled = IsOperatingSystemSelectionEnabled,
            AllowedLanguageCodes = GetSelectedOptionValues(OperatingSystemLanguageOptions),
            DefaultLanguageCode = NormalizeDefaultOption(SelectedDefaultOperatingSystemLanguage?.Value),
            AllowedReleaseIds = GetSelectedOptionValues(OperatingSystemReleaseOptions),
            DefaultReleaseId = NormalizeDefaultOption(SelectedDefaultOperatingSystemRelease?.Value),
            DefaultMediaOffset = SelectedDefaultOperatingSystemMediaOffset?.Value ?? 0,
            AllowedLicenseChannels = GetSelectedOptionValues(OperatingSystemLicenseChannelOptions),
            DefaultLicenseChannel = NormalizeDefaultOption(SelectedDefaultOperatingSystemLicenseChannel?.Value),
            AllowedEditions = GetSelectedOptionValues(OperatingSystemEditionOptions),
            DefaultEdition = NormalizeDefaultOption(SelectedDefaultOperatingSystemEdition?.Value)
        });
    }

    private void RefreshOperatingSystemSelectionLocalizedText()
    {
        OperatingSystemVersionGroupHeader = localizationService.GetString("Customization.OperatingSystemVersionGroupHeader");
        OperatingSystemVersionGroupDescription = localizationService.GetString("Customization.OperatingSystemVersionGroupDescription");
        OperatingSystemLanguageGroupHeader = localizationService.GetString("Customization.OperatingSystemLanguageGroupHeader");
        OperatingSystemLanguageGroupDescription = localizationService.GetString("Customization.OperatingSystemLanguageGroupDescription");
        OperatingSystemLicenseChannelGroupHeader = localizationService.GetString("Customization.OperatingSystemLicenseChannelGroupHeader");
        OperatingSystemLicenseChannelGroupDescription = localizationService.GetString("Customization.OperatingSystemLicenseChannelGroupDescription");
        OperatingSystemEditionGroupHeader = localizationService.GetString("Customization.OperatingSystemEditionGroupHeader");
        OperatingSystemEditionGroupDescription = localizationService.GetString("Customization.OperatingSystemEditionGroupDescription");
        OperatingSystemAllowedLanguagesLabel = localizationService.GetString("Customization.OperatingSystemAllowedLanguagesLabel");
        OperatingSystemAllowedLanguagesDescription = localizationService.GetString("Customization.OperatingSystemAllowedLanguagesDescription");
        OperatingSystemDefaultLanguageLabel = localizationService.GetString("Customization.OperatingSystemDefaultLanguageLabel");
        OperatingSystemDefaultLanguageDescription = localizationService.GetString("Customization.OperatingSystemDefaultLanguageDescription");
        OperatingSystemAllowedReleasesLabel = localizationService.GetString("Customization.OperatingSystemAllowedReleasesLabel");
        OperatingSystemAllowedReleasesDescription = localizationService.GetString("Customization.OperatingSystemAllowedReleasesDescription");
        OperatingSystemDefaultReleaseLabel = localizationService.GetString("Customization.OperatingSystemDefaultReleaseLabel");
        OperatingSystemDefaultReleaseDescription = localizationService.GetString("Customization.OperatingSystemDefaultReleaseDescription");
        OperatingSystemDefaultMediaOffsetLabel = localizationService.GetString("Customization.OperatingSystemDefaultMediaOffsetLabel");
        OperatingSystemDefaultMediaOffsetDescription = localizationService.GetString("Customization.OperatingSystemDefaultMediaOffsetDescription");
        OperatingSystemAllowedLicenseChannelsLabel = localizationService.GetString("Customization.OperatingSystemAllowedLicenseChannelsLabel");
        OperatingSystemAllowedLicenseChannelsDescription = localizationService.GetString("Customization.OperatingSystemAllowedLicenseChannelsDescription");
        OperatingSystemDefaultLicenseChannelLabel = localizationService.GetString("Customization.OperatingSystemDefaultLicenseChannelLabel");
        OperatingSystemDefaultLicenseChannelDescription = localizationService.GetString("Customization.OperatingSystemDefaultLicenseChannelDescription");
        OperatingSystemAllowedEditionsLabel = localizationService.GetString("Customization.OperatingSystemAllowedEditionsLabel");
        OperatingSystemAllowedEditionsDescription = localizationService.GetString("Customization.OperatingSystemAllowedEditionsDescription");
        OperatingSystemDefaultEditionLabel = localizationService.GetString("Customization.OperatingSystemDefaultEditionLabel");
        OperatingSystemDefaultEditionDescription = localizationService.GetString("Customization.OperatingSystemDefaultEditionDescription");
        AutomaticOptionText = localizationService.GetString("Common.AutomaticOption");
        RefreshOperatingSystemLanguageDisplayNames();
        RefreshOperatingSystemMediaOffsetOptions(BuildOperatingSystemSelectionSettings().DefaultMediaOffset);
        RefreshOperatingSystemDefaultOptions(BuildOperatingSystemSelectionSettings());
    }

    private void RefreshOperatingSystemLanguageDisplayNames()
    {
        if (OperatingSystemLanguageOptions.Count == 0)
        {
            return;
        }

        Dictionary<string, string> localizedNames = GetLocalizedLanguageNames();
        foreach (SelectableStringOptionViewModel option in OperatingSystemLanguageOptions)
        {
            option.DisplayName = GetOperatingSystemLanguageDisplayName(localizedNames, option.Value);
        }
    }

    private Dictionary<string, string> GetLocalizedLanguageNames()
    {
        return localizationService.CreateSupportedLanguageOptions()
            .ToDictionary(
                option => CultureCode.Canonicalize(option.Code),
                option => option.DisplayName,
                StringComparer.OrdinalIgnoreCase);
    }

    private string GetOperatingSystemLanguageDisplayName(IReadOnlyDictionary<string, string> localizedNames, string code)
    {
        if (localizedNames.TryGetValue(code, out string? localizedName))
        {
            return localizedName;
        }

        return operatingSystemLanguageFallbackNames.TryGetValue(code, out string? fallbackName) ? fallbackName : code;
    }

    private void RefreshOperatingSystemMediaOffsetOptions(int selectedOffset)
    {
        DefaultOperatingSystemMediaOffsetOptions.Clear();
        DefaultOperatingSystemMediaOffsetOptions.Add(
            new(0, localizationService.GetString("Customization.OperatingSystemMediaLatestOption")));
        for (int offset = 1; offset <= 11; offset++)
        {
            DefaultOperatingSystemMediaOffsetOptions.Add(new(
                offset,
                localizationService.FormatString("Customization.OperatingSystemMediaPreviousOptionFormat", offset)));
        }

        int effectiveOffset = Math.Clamp(selectedOffset, 0, 11);
        SelectedDefaultOperatingSystemMediaOffset =
            DefaultOperatingSystemMediaOffsetOptions.First(option => option.Value == effectiveOffset);
    }

    private void RefreshOperatingSystemDefaultOptions(OperatingSystemSelectionSettings settings)
    {
        isRefreshingOperatingSystemSelectionOptions = true;
        try
        {
            RefreshDefaultOptions(DefaultOperatingSystemLanguageOptions, OperatingSystemLanguageOptions);
            SelectedDefaultOperatingSystemLanguage = SelectStringOption(DefaultOperatingSystemLanguageOptions, settings.DefaultLanguageCode) ?? DefaultOperatingSystemLanguageOptions[0];

            RefreshDefaultOptions(DefaultOperatingSystemReleaseOptions, OperatingSystemReleaseOptions);
            SelectedDefaultOperatingSystemRelease = SelectStringOption(DefaultOperatingSystemReleaseOptions, settings.DefaultReleaseId) ?? DefaultOperatingSystemReleaseOptions[0];

            RefreshOperatingSystemDefaultLicenseChannelOptions(settings);

            RefreshDefaultOptions(DefaultOperatingSystemEditionOptions, OperatingSystemEditionOptions);
            SelectedDefaultOperatingSystemEdition = SelectStringOption(DefaultOperatingSystemEditionOptions, settings.DefaultEdition) ?? DefaultOperatingSystemEditionOptions[0];
        }
        finally
        {
            isRefreshingOperatingSystemSelectionOptions = false;
        }

        OnPropertyChanged(nameof(IsDefaultOperatingSystemLanguageSelectionEnabled));
        OnPropertyChanged(nameof(IsDefaultOperatingSystemReleaseSelectionEnabled));
        OnPropertyChanged(nameof(IsDefaultOperatingSystemLicenseChannelSelectionEnabled));
        OnPropertyChanged(nameof(IsDefaultOperatingSystemEditionSelectionEnabled));
    }

    private void OnOperatingSystemSelectionOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isApplyingState ||
            isRefreshingOperatingSystemSelectionOptions ||
            !string.Equals(e.PropertyName, nameof(SelectableStringOptionViewModel.IsSelected), StringComparison.Ordinal))
        {
            return;
        }

        OperatingSystemSelectionSettings normalized = BuildOperatingSystemSelectionSettings();
        isRefreshingOperatingSystemSelectionOptions = true;
        try
        {
            SetSelectedOptions(OperatingSystemLicenseChannelOptions, normalized.AllowedLicenseChannels);
            RefreshOperatingSystemLicenseChannelAvailability();
        }
        finally
        {
            isRefreshingOperatingSystemSelectionOptions = false;
        }

        RefreshOperatingSystemDefaultOptions(normalized);
        RefreshWindowsOptionalFeatureCompatibility();
        SaveState();
    }

    private void RefreshOperatingSystemDefaultLicenseChannelOptions(OperatingSystemSelectionSettings settings)
    {
        WindowsEditionDefinition? defaultEdition = WindowsEditionCatalog.Find(settings.DefaultEdition);
        IEnumerable<SelectableStringOptionViewModel> compatibleOptions = defaultEdition is null
            ? OperatingSystemLicenseChannelOptions
            : OperatingSystemLicenseChannelOptions.Where(option =>
                defaultEdition.LicenseChannels.Contains(option.Value, StringComparer.OrdinalIgnoreCase));

        RefreshDefaultOptions(DefaultOperatingSystemLicenseChannelOptions, compatibleOptions);
        SelectedDefaultOperatingSystemLicenseChannel =
            SelectStringOption(DefaultOperatingSystemLicenseChannelOptions, settings.DefaultLicenseChannel) ??
            DefaultOperatingSystemLicenseChannelOptions[0];
        OnPropertyChanged(nameof(IsDefaultOperatingSystemLicenseChannelSelectionEnabled));
    }

    private void RefreshOperatingSystemLicenseChannelAvailability()
    {
        string[] selectedEditions = GetSelectedOptionValues(OperatingSystemEditionOptions);
        if (selectedEditions.Length == 0)
        {
            foreach (SelectableStringOptionViewModel option in OperatingSystemLicenseChannelOptions)
            {
                option.IsEnabled = true;
            }

            return;
        }

        IReadOnlyList<string> compatibleChannels = WindowsEditionCatalog.GetCompatibleLicenseChannels(selectedEditions);
        IReadOnlyList<string> requiredChannels = WindowsEditionCatalog.GetRequiredLicenseChannels(selectedEditions);
        foreach (SelectableStringOptionViewModel option in OperatingSystemLicenseChannelOptions)
        {
            option.IsEnabled = compatibleChannels.Contains(option.Value, StringComparer.OrdinalIgnoreCase) &&
                               !requiredChannels.Contains(option.Value, StringComparer.OrdinalIgnoreCase);
        }
    }

    private void AddOperatingSystemSelectionOptions(
        ObservableCollection<SelectableStringOptionViewModel> target,
        IReadOnlyList<string> values,
        Func<string, string> displayNameFactory)
    {
        int sortOrder = 0;
        foreach (string value in values)
        {
            var option = new SelectableStringOptionViewModel(value, displayNameFactory(value), sortOrder, false);
            option.PropertyChanged += OnOperatingSystemSelectionOptionPropertyChanged;
            target.Add(option);
            sortOrder++;
        }
    }

    private static void SetSelectedOptions(
        IEnumerable<SelectableStringOptionViewModel> options,
        IEnumerable<string> selectedValues)
    {
        HashSet<string> selected = selectedValues.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (SelectableStringOptionViewModel option in options)
        {
            option.IsSelected = selected.Contains(option.Value);
        }
    }

    private static string[] GetSelectedOptionValues(IEnumerable<SelectableStringOptionViewModel> options)
    {
        return options
            .Where(option => option.IsSelected)
            .OrderBy(option => option.SortOrder)
            .ThenBy(option => option.Value, StringComparer.OrdinalIgnoreCase)
            .Select(option => option.Value)
            .ToArray();
    }

    private void RefreshDefaultOptions(
        ObservableCollection<SelectionOption<string>> target,
        IEnumerable<SelectableStringOptionViewModel> allOptions)
    {
        SelectableStringOptionViewModel[] selectedOptions = allOptions
            .Where(option => option.IsSelected)
            .ToArray();
        SelectableStringOptionViewModel[] selectableOptions = selectedOptions.Length > 0
            ? selectedOptions
            : allOptions.ToArray();

        target.Clear();
        if (selectedOptions.Length == 1)
        {
            target.Add(new(selectedOptions[0].Value, selectedOptions[0].DisplayName));
            return;
        }

        target.Add(new(AutomaticSelectionValue, AutomaticOptionText));
        foreach (SelectableStringOptionViewModel option in selectableOptions)
        {
            target.Add(new(option.Value, option.DisplayName));
        }
    }

    private static bool HasSingleSelectedOption(IEnumerable<SelectableStringOptionViewModel> options)
    {
        return options.Count(option => option.IsSelected) == 1;
    }

    private static SelectionOption<string>? SelectStringOption(IEnumerable<SelectionOption<string>> options, string? value)
    {
        return options.FirstOrDefault(option =>
            string.Equals(option.Value, value?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeDefaultOption(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string FormatLicenseChannel(string value)
    {
        return value switch
        {
            "RET" => "Retail",
            "VOL" => "Volume",
            _ => value
        };
    }
}
