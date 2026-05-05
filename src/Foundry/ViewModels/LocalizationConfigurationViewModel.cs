using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

public sealed partial class LocalizationConfigurationViewModel : ObservableObject, IDisposable
{
    private const string AutomaticSelectionValue = "";

    private readonly IExpertDeployConfigurationStateService configurationStateService;
    private readonly IApplicationLocalizationService localizationService;
    private bool isApplyingState = true;
    private bool isRefreshingOptions;
    private bool isSavingState;

    public LocalizationConfigurationViewModel(
        IExpertDeployConfigurationStateService configurationStateService,
        ILanguageRegistryService languageRegistryService,
        IApplicationLocalizationService localizationService)
    {
        this.configurationStateService = configurationStateService;
        this.localizationService = localizationService;

        PageTitle = localizationService.GetString("LocalizationPage_Title.Text");
        VisibleLanguagesHeader = localizationService.GetString("Localization.VisibleLanguages.Header");
        DefaultLanguageHeader = localizationService.GetString("Localization.DefaultLanguage.Header");
        TimeZoneHeader = localizationService.GetString("Localization.TimeZone.Header");
        ForceSingleVisibleLanguageText = localizationService.GetString("Localization.ForceSingleVisibleLanguage");
        AutomaticOptionText = localizationService.GetString("Localization.AutomaticOption");

        BuildLanguageOptions(languageRegistryService.GetLanguages());
        RefreshTimeZones();
        ApplyState(configurationStateService.Current.Localization);

        localizationService.LanguageChanged += OnLanguageChanged;
        configurationStateService.StateChanged += OnConfigurationStateChanged;
        isApplyingState = false;
    }

    public ObservableCollection<LocalizationLanguageOptionViewModel> AvailableLanguages { get; } = [];
    public ObservableCollection<SelectionOption<string>> DefaultLanguageOptions { get; } = [];
    public ObservableCollection<SelectionOption<string>> TimeZoneOptions { get; } = [];

    [ObservableProperty]
    public partial string PageTitle { get; set; }

    [ObservableProperty]
    public partial string VisibleLanguagesHeader { get; set; }

    [ObservableProperty]
    public partial string DefaultLanguageHeader { get; set; }

    [ObservableProperty]
    public partial string TimeZoneHeader { get; set; }

    [ObservableProperty]
    public partial string ForceSingleVisibleLanguageText { get; set; }

    [ObservableProperty]
    public partial string AutomaticOptionText { get; set; }

    [ObservableProperty]
    public partial SelectionOption<string>? SelectedDefaultLanguage { get; set; }

    [ObservableProperty]
    public partial SelectionOption<string>? SelectedTimeZone { get; set; }

    [ObservableProperty]
    public partial bool ForceSingleVisibleLanguage { get; set; }

    public void Dispose()
    {
        localizationService.LanguageChanged -= OnLanguageChanged;
        configurationStateService.StateChanged -= OnConfigurationStateChanged;
        foreach (LocalizationLanguageOptionViewModel option in AvailableLanguages)
        {
            option.PropertyChanged -= OnLanguageOptionPropertyChanged;
        }
    }

    partial void OnSelectedDefaultLanguageChanged(SelectionOption<string>? value)
    {
        SaveState();
    }

    partial void OnSelectedTimeZoneChanged(SelectionOption<string>? value)
    {
        SaveState();
    }

    partial void OnForceSingleVisibleLanguageChanged(bool value)
    {
        SaveState();
    }

    private void BuildLanguageOptions(IReadOnlyList<LanguageRegistryEntry> languages)
    {
        foreach (LanguageRegistryEntry language in languages)
        {
            string code = Canonicalize(language.Code);
            var option = new LocalizationLanguageOptionViewModel(
                code,
                $"{language.DisplayName} ({code})",
                language.SortOrder,
                false);
            option.PropertyChanged += OnLanguageOptionPropertyChanged;
            AvailableLanguages.Add(option);
        }
    }

    private void ApplyState(LocalizationSettings settings)
    {
        isApplyingState = true;

        HashSet<string> visibleCodes = settings.VisibleLanguageCodes
            .Select(NormalizeForComparison)
            .Where(code => code.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (LocalizationLanguageOptionViewModel option in AvailableLanguages)
        {
            option.IsSelected = visibleCodes.Contains(NormalizeForComparison(option.Code));
        }

        ForceSingleVisibleLanguage = settings.ForceSingleVisibleLanguage;
        RefreshDefaultLanguageOptions(settings.DefaultLanguageCodeOverride);
        SelectedTimeZone = SelectStringOption(TimeZoneOptions, settings.DefaultTimeZoneId);

        isApplyingState = false;
    }

    private void SaveState()
    {
        if (isApplyingState || isRefreshingOptions)
        {
            return;
        }

        string[] visibleLanguageCodes = AvailableLanguages
            .Where(option => option.IsSelected)
            .OrderBy(option => option.SortOrder)
            .ThenBy(option => option.Code, StringComparer.OrdinalIgnoreCase)
            .Select(option => Canonicalize(option.Code))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string? selectedDefaultLanguage = SelectedDefaultLanguage?.Value;
        string? defaultLanguageCodeOverride = visibleLanguageCodes.Any(code =>
            string.Equals(
                NormalizeForComparison(code),
                NormalizeForComparison(selectedDefaultLanguage),
                StringComparison.OrdinalIgnoreCase))
            ? Canonicalize(selectedDefaultLanguage)
            : null;

        string? defaultTimeZoneId = string.IsNullOrWhiteSpace(SelectedTimeZone?.Value)
            ? null
            : SelectedTimeZone.Value.Trim();

        isSavingState = true;
        try
        {
            configurationStateService.UpdateLocalization(new LocalizationSettings
            {
                VisibleLanguageCodes = visibleLanguageCodes,
                DefaultLanguageCodeOverride = defaultLanguageCodeOverride,
                DefaultTimeZoneId = defaultTimeZoneId,
                ForceSingleVisibleLanguage = ForceSingleVisibleLanguage
            });
        }
        finally
        {
            isSavingState = false;
        }
    }

    private void OnLanguageOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isApplyingState ||
            !string.Equals(e.PropertyName, nameof(LocalizationLanguageOptionViewModel.IsSelected), StringComparison.Ordinal))
        {
            return;
        }

        SelectionOption<string>? previousDefault = SelectedDefaultLanguage;
        RefreshDefaultLanguageOptions(previousDefault?.Value);
        SaveState();
    }

    private void RefreshDefaultLanguageOptions(string? preferredDefaultLanguageCode)
    {
        string? selectedValue = preferredDefaultLanguageCode;

        isRefreshingOptions = true;
        try
        {
            DefaultLanguageOptions.Clear();
            DefaultLanguageOptions.Add(new(AutomaticSelectionValue, AutomaticOptionText));

            foreach (LocalizationLanguageOptionViewModel option in AvailableLanguages.Where(option => option.IsSelected))
            {
                DefaultLanguageOptions.Add(new(option.Code, option.DisplayName));
            }

            SelectedDefaultLanguage = SelectLanguageOption(DefaultLanguageOptions, selectedValue) ?? DefaultLanguageOptions[0];
        }
        finally
        {
            isRefreshingOptions = false;
        }
    }

    private void RefreshTimeZones()
    {
        string? selectedValue = SelectedTimeZone?.Value;

        isRefreshingOptions = true;
        try
        {
            TimeZoneOptions.Clear();
            TimeZoneOptions.Add(new(AutomaticSelectionValue, AutomaticOptionText));
            foreach (TimeZoneInfo timeZone in TimeZoneInfo.GetSystemTimeZones()
                         .OrderBy(timeZone => timeZone.BaseUtcOffset)
                         .ThenBy(timeZone => timeZone.DisplayName, StringComparer.CurrentCulture))
            {
                TimeZoneOptions.Add(new(timeZone.Id, $"{timeZone.DisplayName} ({timeZone.Id})"));
            }

            SelectedTimeZone = SelectStringOption(TimeZoneOptions, selectedValue) ?? TimeZoneOptions[0];
        }
        finally
        {
            isRefreshingOptions = false;
        }
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        PageTitle = localizationService.GetString("LocalizationPage_Title.Text");
        VisibleLanguagesHeader = localizationService.GetString("Localization.VisibleLanguages.Header");
        DefaultLanguageHeader = localizationService.GetString("Localization.DefaultLanguage.Header");
        TimeZoneHeader = localizationService.GetString("Localization.TimeZone.Header");
        ForceSingleVisibleLanguageText = localizationService.GetString("Localization.ForceSingleVisibleLanguage");
        AutomaticOptionText = localizationService.GetString("Localization.AutomaticOption");
        RefreshDefaultLanguageOptions(SelectedDefaultLanguage?.Value);
        RefreshTimeZones();
    }

    private void OnConfigurationStateChanged(object? sender, EventArgs e)
    {
        if (isSavingState)
        {
            return;
        }

        ApplyState(configurationStateService.Current.Localization);
    }

    private static SelectionOption<string>? SelectLanguageOption(IEnumerable<SelectionOption<string>> options, string? value)
    {
        return options.FirstOrDefault(option =>
            string.Equals(
                NormalizeForComparison(option.Value),
                NormalizeForComparison(value),
                StringComparison.OrdinalIgnoreCase));
    }

    private static SelectionOption<string>? SelectStringOption(IEnumerable<SelectionOption<string>> options, string? value)
    {
        return options.FirstOrDefault(option =>
            string.Equals(option.Value, value?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string Canonicalize(string? languageCode)
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

    private static string NormalizeForComparison(string? languageCode)
    {
        return Canonicalize(languageCode).ToLowerInvariant();
    }
}
