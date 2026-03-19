using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Foundry.Models.Configuration;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

public partial class LocalizationSettingsViewModel : LocalizedViewModelBase
{
    public LocalizationSettingsViewModel(ILocalizationService localizationService, IReadOnlyList<LanguageRegistryEntry> languages)
        : base(localizationService)
    {
        foreach (LanguageRegistryEntry language in languages)
        {
            var option = new SelectableLanguageOptionViewModel(language);
            option.PropertyChanged += OnLanguageOptionPropertyChanged;
            AvailableLanguages.Add(option);
        }

        VisibleLanguages.CollectionChanged += OnVisibleLanguagesCollectionChanged;
        RefreshVisibleLanguages();
    }

    [ObservableProperty]
    private string selectedDefaultLanguageCode = string.Empty;

    [ObservableProperty]
    private bool forceSingleVisibleLanguage;

    public ObservableCollection<SelectableLanguageOptionViewModel> AvailableLanguages { get; } = [];

    public ObservableCollection<LanguageRegistryEntry> VisibleLanguages { get; } = [];

    public LocalizationSettings BuildSettings()
    {
        string[] visibleCodes = VisibleLanguages
            .Select(language => language.Code)
            .ToArray();

        string? defaultCode = visibleCodes.Any(code =>
                code.Equals(SelectedDefaultLanguageCode, StringComparison.OrdinalIgnoreCase))
            ? SelectedDefaultLanguageCode
            : null;

        return new LocalizationSettings
        {
            VisibleLanguageCodes = visibleCodes,
            DefaultLanguageCodeOverride = defaultCode,
            ForceSingleVisibleLanguage = ForceSingleVisibleLanguage
        };
    }

    public void ApplySettings(LocalizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        HashSet<string> selectedCodes = settings.VisibleLanguageCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (SelectableLanguageOptionViewModel option in AvailableLanguages)
        {
            option.IsSelected = selectedCodes.Contains(option.Code);
        }

        RefreshVisibleLanguages();
        ForceSingleVisibleLanguage = settings.ForceSingleVisibleLanguage;

        SelectedDefaultLanguageCode = VisibleLanguages.Any(language =>
                language.Code.Equals(settings.DefaultLanguageCodeOverride, StringComparison.OrdinalIgnoreCase))
            ? settings.DefaultLanguageCodeOverride ?? string.Empty
            : string.Empty;
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
}
