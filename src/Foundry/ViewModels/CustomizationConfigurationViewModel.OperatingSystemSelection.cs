using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.ViewModels;

public sealed partial class CustomizationConfigurationViewModel
{
    private const string AutomaticSelectionValue = "";

    private bool isRefreshingOperatingSystemSelectionOptions;

    public ObservableCollection<SelectableStringOptionViewModel> OperatingSystemLanguageOptions { get; } = [];
    public ObservableCollection<SelectionOption<string>> DefaultOperatingSystemLanguageOptions { get; } = [];
    public ObservableCollection<SelectableStringOptionViewModel> OperatingSystemReleaseOptions { get; } = [];
    public ObservableCollection<SelectionOption<string>> DefaultOperatingSystemReleaseOptions { get; } = [];
    public ObservableCollection<SelectableStringOptionViewModel> OperatingSystemLicenseChannelOptions { get; } = [];
    public ObservableCollection<SelectionOption<string>> DefaultOperatingSystemLicenseChannelOptions { get; } = [];
    public ObservableCollection<SelectableStringOptionViewModel> OperatingSystemEditionOptions { get; } = [];
    public ObservableCollection<SelectionOption<string>> DefaultOperatingSystemEditionOptions { get; } = [];

    public bool IsOperatingSystemSelectionOptionsEnabled => IsOperatingSystemSelectionEnabled;
    public bool IsDefaultOperatingSystemLanguageSelectionEnabled => IsOperatingSystemSelectionOptionsEnabled && !HasSingleSelectedOption(OperatingSystemLanguageOptions);
    public bool IsDefaultOperatingSystemReleaseSelectionEnabled => IsOperatingSystemSelectionOptionsEnabled && !HasSingleSelectedOption(OperatingSystemReleaseOptions);
    public bool IsDefaultOperatingSystemLicenseChannelSelectionEnabled => IsOperatingSystemSelectionOptionsEnabled && !HasSingleSelectedOption(OperatingSystemLicenseChannelOptions);
    public bool IsDefaultOperatingSystemEditionSelectionEnabled => IsOperatingSystemSelectionOptionsEnabled && !HasSingleSelectedOption(OperatingSystemEditionOptions);

    [ObservableProperty]
    public partial string OperatingSystemSelectionHeader { get; set; }

    [ObservableProperty]
    public partial string OperatingSystemSelectionDescription { get; set; }

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
    public partial bool IsOperatingSystemSelectionExpanded { get; set; }

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

    partial void OnSelectedDefaultOperatingSystemLanguageChanged(SelectionOption<string>? value)
    {
        SaveState();
    }

    partial void OnSelectedDefaultOperatingSystemReleaseChanged(SelectionOption<string>? value)
    {
        SaveState();
    }

    partial void OnSelectedDefaultOperatingSystemLicenseChannelChanged(SelectionOption<string>? value)
    {
        SaveState();
    }

    partial void OnSelectedDefaultOperatingSystemEditionChanged(SelectionOption<string>? value)
    {
        SaveState();
    }

    partial void OnIsOperatingSystemSelectionEnabledChanged(bool value)
    {
        IsOperatingSystemSelectionExpanded = value;
        SaveState();
    }

    private void InitializeOperatingSystemSelectionOptions(IReadOnlyList<LanguageRegistryEntry> languages)
    {
        foreach (LanguageRegistryEntry language in languages
            .OrderBy(language => language.SortOrder)
            .ThenBy(language => language.Code, StringComparer.OrdinalIgnoreCase))
        {
            string code = CanonicalizeLanguageCode(language.Code);
            var option = new SelectableStringOptionViewModel(
                code,
                $"{language.DisplayName} ({code})",
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
        IsOperatingSystemSelectionExpanded = settings.IsEnabled;
        SetSelectedOptions(OperatingSystemLanguageOptions, settings.AllowedLanguageCodes);
        SetSelectedOptions(OperatingSystemReleaseOptions, settings.AllowedReleaseIds);
        SetSelectedOptions(OperatingSystemLicenseChannelOptions, settings.AllowedLicenseChannels);
        SetSelectedOptions(OperatingSystemEditionOptions, settings.AllowedEditions);

        RefreshOperatingSystemDefaultOptions(settings);
    }

    private OperatingSystemSelectionSettings BuildOperatingSystemSelectionSettings()
    {
        return OperatingSystemSelectionSettingsNormalizer.Normalize(new OperatingSystemSelectionSettings
        {
            IsEnabled = IsOperatingSystemSelectionEnabled,
            AllowedLanguageCodes = GetSelectedOptionValues(OperatingSystemLanguageOptions),
            DefaultLanguageCode = NormalizeDefaultOption(SelectedDefaultOperatingSystemLanguage?.Value),
            AllowedReleaseIds = GetSelectedOptionValues(OperatingSystemReleaseOptions),
            DefaultReleaseId = NormalizeDefaultOption(SelectedDefaultOperatingSystemRelease?.Value),
            AllowedLicenseChannels = GetSelectedOptionValues(OperatingSystemLicenseChannelOptions),
            DefaultLicenseChannel = NormalizeDefaultOption(SelectedDefaultOperatingSystemLicenseChannel?.Value),
            AllowedEditions = GetSelectedOptionValues(OperatingSystemEditionOptions),
            DefaultEdition = NormalizeDefaultOption(SelectedDefaultOperatingSystemEdition?.Value)
        });
    }

    private void RefreshOperatingSystemSelectionLocalizedText()
    {
        OperatingSystemSelectionHeader = localizationService.GetString("Customization.OperatingSystemSelectionHeader");
        OperatingSystemSelectionDescription = localizationService.GetString("Customization.OperatingSystemSelectionDescription");
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
        OperatingSystemAllowedLicenseChannelsLabel = localizationService.GetString("Customization.OperatingSystemAllowedLicenseChannelsLabel");
        OperatingSystemAllowedLicenseChannelsDescription = localizationService.GetString("Customization.OperatingSystemAllowedLicenseChannelsDescription");
        OperatingSystemDefaultLicenseChannelLabel = localizationService.GetString("Customization.OperatingSystemDefaultLicenseChannelLabel");
        OperatingSystemDefaultLicenseChannelDescription = localizationService.GetString("Customization.OperatingSystemDefaultLicenseChannelDescription");
        OperatingSystemAllowedEditionsLabel = localizationService.GetString("Customization.OperatingSystemAllowedEditionsLabel");
        OperatingSystemAllowedEditionsDescription = localizationService.GetString("Customization.OperatingSystemAllowedEditionsDescription");
        OperatingSystemDefaultEditionLabel = localizationService.GetString("Customization.OperatingSystemDefaultEditionLabel");
        OperatingSystemDefaultEditionDescription = localizationService.GetString("Customization.OperatingSystemDefaultEditionDescription");
        AutomaticOptionText = localizationService.GetString("Common.AutomaticOption");
        RefreshOperatingSystemDefaultOptions(BuildOperatingSystemSelectionSettings());
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

            RefreshDefaultOptions(DefaultOperatingSystemLicenseChannelOptions, OperatingSystemLicenseChannelOptions);
            SelectedDefaultOperatingSystemLicenseChannel = SelectStringOption(DefaultOperatingSystemLicenseChannelOptions, settings.DefaultLicenseChannel) ?? DefaultOperatingSystemLicenseChannelOptions[0];

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
            !string.Equals(e.PropertyName, nameof(SelectableStringOptionViewModel.IsSelected), StringComparison.Ordinal))
        {
            return;
        }

        RefreshOperatingSystemDefaultOptions(BuildOperatingSystemSelectionSettings());
        SaveState();
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

    private static string CanonicalizeLanguageCode(string? languageCode)
    {
        string normalized = string.IsNullOrWhiteSpace(languageCode)
            ? string.Empty
            : languageCode.Trim().Replace('_', '-');

        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return CultureInfo.GetCultureInfo(normalized).Name;
        }
        catch (CultureNotFoundException)
        {
            return normalized;
        }
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
