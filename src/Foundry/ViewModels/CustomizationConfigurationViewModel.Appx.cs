using System.Collections.ObjectModel;
using System.ComponentModel;
using Foundry.Core.Models.Configuration;

namespace Foundry.ViewModels;

public sealed partial class CustomizationConfigurationViewModel
{
    private const string CustomAppxRemovalProfile = "custom";
    private const string NoneAppxRemovalProfile = "none";
    private const string CategoryAppxRemovalProfilePrefix = "category:";

    private bool isApplyingAppxSelection;

    public ObservableCollection<AppxRemovalCategoryViewModel> AppxRemovalCategories { get; } = [];

    public ObservableCollection<SelectionOption<string>> AppxRemovalProfileOptions { get; } = [];

    public bool IsAppxRemovalOptionsEnabled => IsAppxRemovalEnabled;

    public string AppxRemovalSelectedCountText => localizationService.FormatString(
        "Customization.AppxRemovalSelectedCountFormat",
        AppxRemovalCategories.SelectMany(category => category.Items).Count(item => item.IsSelected),
        AppxRemovalCategories.SelectMany(category => category.Items).Count());

    [ObservableProperty]
    public partial string AppxRemovalHeader { get; set; }

    [ObservableProperty]
    public partial string AppxRemovalDescription { get; set; }

    [ObservableProperty]
    public partial string AppxRemovalEnableText { get; set; }

    [ObservableProperty]
    public partial string AppxRemovalProfileLabel { get; set; }

    [ObservableProperty]
    public partial string AppxRemovalProfileDescription { get; set; }

    [ObservableProperty]
    public partial string AppxRemovalSelectAllText { get; set; }

    [ObservableProperty]
    public partial string AppxRemovalRemoveAllText { get; set; }

    [ObservableProperty]
    public partial string AppxRemovalPackagesLabel { get; set; }

    [ObservableProperty]
    public partial bool IsAppxRemovalExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAppxRemovalOptionsEnabled))]
    public partial bool IsAppxRemovalEnabled { get; set; }

    [ObservableProperty]
    public partial SelectionOption<string>? SelectedAppxRemovalProfile { get; set; }

    [RelayCommand]
    private void SelectAllAppxRemoval()
    {
        SetAllAppxSelections(true, CustomAppxRemovalProfile);
    }

    [RelayCommand]
    private void RemoveAllAppxRemoval()
    {
        SetAllAppxSelections(false, NoneAppxRemovalProfile);
    }

    private void InitializeAppxRemovalCatalog()
    {
        foreach (var group in AppxRemovalCatalog.Entries.GroupBy(entry => entry.Category))
        {
            var categoryViewModel = new AppxRemovalCategoryViewModel(
                group.Key,
                group
                    .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(entry => new AppxRemovalItemViewModel(
                        entry.PackageName,
                        entry.DisplayName,
                        entry.Category,
                        entry.DefaultSelected)));

            foreach (AppxRemovalItemViewModel item in categoryViewModel.Items)
            {
                item.PropertyChanged += OnAppxRemovalItemPropertyChanged;
            }

            AppxRemovalCategories.Add(categoryViewModel);
        }
    }

    private void ApplyAppxRemovalState(AppxRemovalSettings settings)
    {
        IsAppxRemovalEnabled = settings.IsEnabled;
        IsAppxRemovalExpanded = settings.IsEnabled;
        HashSet<string> selectedPackageNames = settings.PackageNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool hasPersistedSelection = selectedPackageNames.Count > 0;
        bool shouldUseDefaultSelection = settings.IsEnabled && !hasPersistedSelection;

        isApplyingAppxSelection = true;
        try
        {
            foreach (AppxRemovalItemViewModel item in AppxRemovalCategories.SelectMany(category => category.Items))
            {
                item.IsSelected = hasPersistedSelection
                    ? selectedPackageNames.Contains(item.PackageName)
                    : shouldUseDefaultSelection && item.DefaultSelected;
            }

            SelectedAppxRemovalProfile = ResolveCurrentAppxRemovalProfile();
        }
        finally
        {
            isApplyingAppxSelection = false;
        }

        OnPropertyChanged(nameof(AppxRemovalSelectedCountText));
    }

    private AppxRemovalSettings BuildAppxRemovalSettings()
    {
        string[] selectedPackageNames = AppxRemovalCategories
            .SelectMany(category => category.Items)
            .Where(item => item.IsSelected)
            .Select(item => item.PackageName)
            .ToArray();

        return IsAppxRemovalEnabled && selectedPackageNames.Length > 0
            ? new AppxRemovalSettings
            {
                IsEnabled = true,
                PackageNames = selectedPackageNames
            }
            : new AppxRemovalSettings();
    }

    private void RefreshAppxRemovalLocalizedText()
    {
        AppxRemovalHeader = localizationService.GetString("Customization.AppxRemovalHeader");
        AppxRemovalDescription = localizationService.GetString("Customization.AppxRemovalDescription");
        AppxRemovalEnableText = localizationService.GetString("Customization.AppxRemovalEnableLabel");
        AppxRemovalProfileLabel = localizationService.GetString("Customization.AppxRemovalProfileLabel");
        AppxRemovalProfileDescription = localizationService.GetString("Customization.AppxRemovalProfileDescription");
        AppxRemovalSelectAllText = localizationService.GetString("Customization.AppxRemovalSelectAll");
        AppxRemovalRemoveAllText = localizationService.GetString("Customization.AppxRemovalRemoveAll");
        AppxRemovalPackagesLabel = localizationService.GetString("Customization.AppxRemovalPackagesLabel");
        RefreshAppxRemovalProfiles();
        OnPropertyChanged(nameof(AppxRemovalSelectedCountText));
    }

    private void RefreshAppxRemovalProfiles()
    {
        string selectedValue = SelectedAppxRemovalProfile?.Value ?? NoneAppxRemovalProfile;

        isApplyingAppxSelection = true;
        try
        {
            AppxRemovalProfileOptions.Clear();
            foreach (string category in AppxRemovalCatalog.Entries
                         .Select(entry => entry.Category)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(category => category, StringComparer.OrdinalIgnoreCase))
            {
                AppxRemovalProfileOptions.Add(new(CategoryAppxRemovalProfilePrefix + category, category));
            }

            AppxRemovalProfileOptions.Add(new(NoneAppxRemovalProfile, localizationService.GetString("Customization.AppxRemovalProfileNone")));
            AppxRemovalProfileOptions.Add(new(CustomAppxRemovalProfile, localizationService.GetString("Customization.AppxRemovalProfileCustom")));
            SelectedAppxRemovalProfile = AppxRemovalProfileOptions.FirstOrDefault(option => option.Value == selectedValue)
                ?? AppxRemovalProfileOptions.First(option => option.Value == NoneAppxRemovalProfile);
        }
        finally
        {
            isApplyingAppxSelection = false;
        }
    }

    private void ApplyAppxRemovalProfile(string profile)
    {
        if (profile.StartsWith(CategoryAppxRemovalProfilePrefix, StringComparison.OrdinalIgnoreCase))
        {
            string category = profile[CategoryAppxRemovalProfilePrefix.Length..];
            SetAppxSelections(
                item => string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase),
                profile);
            return;
        }

        if (string.Equals(profile, NoneAppxRemovalProfile, StringComparison.Ordinal))
        {
            SetAllAppxSelections(false, NoneAppxRemovalProfile);
        }
    }

    private void SetAllAppxSelections(bool isSelected, string selectedProfileValue)
    {
        SetAppxSelections(_ => isSelected, selectedProfileValue);
    }

    private void SetAppxSelections(Func<AppxRemovalItemViewModel, bool> selector, string selectedProfileValue)
    {
        isApplyingAppxSelection = true;
        try
        {
            foreach (AppxRemovalItemViewModel item in AppxRemovalCategories.SelectMany(category => category.Items))
            {
                item.IsSelected = selector(item);
            }
        }
        finally
        {
            isApplyingAppxSelection = false;
        }

        SelectedAppxRemovalProfile = AppxRemovalProfileOptions.FirstOrDefault(option => option.Value == selectedProfileValue);
        OnPropertyChanged(nameof(AppxRemovalSelectedCountText));
        SaveState();
    }

    private SelectionOption<string>? ResolveCurrentAppxRemovalProfile()
    {
        AppxRemovalItemViewModel[] selectedItems = AppxRemovalCategories
            .SelectMany(category => category.Items)
            .Where(item => item.IsSelected)
            .ToArray();

        if (selectedItems.Length == 0)
        {
            return AppxRemovalProfileOptions.FirstOrDefault(option => option.Value == NoneAppxRemovalProfile);
        }

        string[] selectedCategories = selectedItems
            .Select(item => item.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selectedCategories.Length == 1)
        {
            string selectedCategory = selectedCategories[0];
            string categoryProfile = CategoryAppxRemovalProfilePrefix + selectedCategory;
            int categoryItemCount = AppxRemovalCategories
                .SelectMany(category => category.Items)
                .Count(item => string.Equals(item.Category, selectedCategory, StringComparison.OrdinalIgnoreCase));

            if (selectedItems.Length == categoryItemCount)
            {
                return AppxRemovalProfileOptions.FirstOrDefault(option => option.Value == categoryProfile);
            }
        }

        return AppxRemovalProfileOptions.FirstOrDefault(option => option.Value == CustomAppxRemovalProfile);
    }

    partial void OnIsAppxRemovalEnabledChanged(bool value)
    {
        IsAppxRemovalExpanded = value;
        SaveState();
    }

    partial void OnSelectedAppxRemovalProfileChanged(SelectionOption<string>? value)
    {
        if (!isApplyingState && !isApplyingAppxSelection && value is not null)
        {
            ApplyAppxRemovalProfile(value.Value);
        }
    }

    private void OnAppxRemovalItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(AppxRemovalItemViewModel.IsSelected), StringComparison.Ordinal))
        {
            return;
        }

        if (!isApplyingState && !isApplyingAppxSelection)
        {
            SelectedAppxRemovalProfile = AppxRemovalProfileOptions.FirstOrDefault(option => option.Value == CustomAppxRemovalProfile);
            SaveState();
        }

        OnPropertyChanged(nameof(AppxRemovalSelectedCountText));
    }
}
