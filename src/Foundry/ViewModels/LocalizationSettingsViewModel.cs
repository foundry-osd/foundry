using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Foundry.Models.Configuration;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

public partial class LocalizationSettingsViewModel : LocalizedViewModelBase
{
    private const string AutomaticTimeZoneId = "";

    public LocalizationSettingsViewModel(ILocalizationService localizationService, IReadOnlyList<LanguageRegistryEntry> languages)
        : base(localizationService)
    {
        foreach (LanguageRegistryEntry language in languages)
        {
            var option = new SelectableLanguageOptionViewModel(language);
            option.PropertyChanged += OnLanguageOptionPropertyChanged;
            AvailableLanguages.Add(option);
        }

        LocalizationService.LanguageChanged += OnAppLanguageChanged;
        RefreshAvailableTimeZones();
        VisibleLanguages.CollectionChanged += OnVisibleLanguagesCollectionChanged;
        RefreshVisibleLanguages();
    }

    [ObservableProperty]
    public partial string SelectedDefaultLanguageCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedTimeZoneId { get; set; } = AutomaticTimeZoneId;

    [ObservableProperty]
    public partial bool ForceSingleVisibleLanguage { get; set; }

    public ObservableCollection<SelectableLanguageOptionViewModel> AvailableLanguages { get; } = [];
    public ObservableCollection<TimeZoneOption> AvailableTimeZones { get; } = [];

    public ObservableCollection<LanguageRegistryEntry> VisibleLanguages { get; } = [];

    public override void Dispose()
    {
        LocalizationService.LanguageChanged -= OnAppLanguageChanged;
        base.Dispose();
    }

    public LocalizationSettings BuildSettings()
    {
        string[] visibleCodes = VisibleLanguages
            .Select(language => LanguageCodeUtility.Canonicalize(language.Code))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string selectedDefaultLanguageCode = LanguageCodeUtility.Canonicalize(SelectedDefaultLanguageCode);
        string? defaultCode = visibleCodes.Any(code =>
                LanguageCodeUtility.NormalizeForComparison(code).Equals(
                    LanguageCodeUtility.NormalizeForComparison(selectedDefaultLanguageCode),
                    StringComparison.OrdinalIgnoreCase))
            ? selectedDefaultLanguageCode
            : null;

        return new LocalizationSettings
        {
            VisibleLanguageCodes = visibleCodes,
            DefaultLanguageCodeOverride = defaultCode,
            DefaultTimeZoneId = NormalizeOptionalTimeZoneId(SelectedTimeZoneId),
            ForceSingleVisibleLanguage = ForceSingleVisibleLanguage
        };
    }

    public void ApplySettings(LocalizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        HashSet<string> selectedCodes = settings.VisibleLanguageCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(LanguageCodeUtility.NormalizeForComparison)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (SelectableLanguageOptionViewModel option in AvailableLanguages)
        {
            option.IsSelected = selectedCodes.Contains(LanguageCodeUtility.NormalizeForComparison(option.Code));
        }

        RefreshVisibleLanguages();
        ForceSingleVisibleLanguage = settings.ForceSingleVisibleLanguage;

        SelectedDefaultLanguageCode = VisibleLanguages.Any(language =>
                LanguageCodeUtility.NormalizeForComparison(language.Code).Equals(
                    LanguageCodeUtility.NormalizeForComparison(settings.DefaultLanguageCodeOverride),
                    StringComparison.OrdinalIgnoreCase))
            ? LanguageCodeUtility.Canonicalize(settings.DefaultLanguageCodeOverride)
            : string.Empty;
        SelectedTimeZoneId = AvailableTimeZones.Any(option =>
                option.Id.Equals(settings.DefaultTimeZoneId, StringComparison.OrdinalIgnoreCase))
            ? settings.DefaultTimeZoneId ?? AutomaticTimeZoneId
            : AutomaticTimeZoneId;
    }

    private void OnLanguageOptionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(SelectableLanguageOptionViewModel.IsSelected), StringComparison.Ordinal))
        {
            return;
        }

        RefreshVisibleLanguages();
    }

    private void OnVisibleLanguagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (VisibleLanguages.Any(language =>
                language.Code.Equals(SelectedDefaultLanguageCode, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        SelectedDefaultLanguageCode = string.Empty;
    }

    private void RefreshVisibleLanguages()
    {
        LanguageRegistryEntry[] selected = AvailableLanguages
            .Where(option => option.IsSelected)
            .Select(option => option.Language)
            .OrderBy(option => option.SortOrder)
            .ThenBy(option => option.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        VisibleLanguages.Clear();
        foreach (LanguageRegistryEntry language in selected)
        {
            VisibleLanguages.Add(language);
        }
    }

    private void OnAppLanguageChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(RefreshAvailableTimeZones);
    }

    private void RefreshAvailableTimeZones()
    {
        string preservedSelection = SelectedTimeZoneId;
        List<TimeZoneOption> timeZones =
        [
            new(AutomaticTimeZoneId, Strings["Localization.TimeZoneAutomatic"])
        ];
        timeZones.AddRange(
            TimeZoneInfo.GetSystemTimeZones()
                .OrderBy(timeZone => timeZone.BaseUtcOffset)
                .ThenBy(timeZone => timeZone.DisplayName, StringComparer.CurrentCulture)
                .Select(timeZone => new TimeZoneOption(timeZone.Id, $"{timeZone.DisplayName} ({timeZone.Id})")));

        AvailableTimeZones.Clear();
        foreach (TimeZoneOption timeZone in timeZones)
        {
            AvailableTimeZones.Add(timeZone);
        }

        SelectedTimeZoneId = AvailableTimeZones.Any(option =>
                option.Id.Equals(preservedSelection, StringComparison.OrdinalIgnoreCase))
            ? preservedSelection
            : AutomaticTimeZoneId;
    }

    private static string? NormalizeOptionalTimeZoneId(string? timeZoneId)
    {
        return string.IsNullOrWhiteSpace(timeZoneId)
            ? null
            : timeZoneId.Trim();
    }
}
