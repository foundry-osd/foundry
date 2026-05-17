using System.Collections.ObjectModel;
using System.ComponentModel;
using Foundry.Core.Models.Configuration;

namespace Foundry.ViewModels;

public sealed partial class CustomizationConfigurationViewModel
{
    private const string CustomAppxRemovalProfile = "custom";
    private const string NoneAppxRemovalProfile = "none";

    private bool isApplyingAppxSelection;

    public ObservableCollection<AppxRemovalCategoryViewModel> AppxRemovalCategories { get; } = [];

    public bool IsAppxRemovalOptionsEnabled => IsAppxRemovalEnabled;

    public string AppxRemovalSelectedCountText => localizationService.FormatString(
        "Customization.AppxRemovalSelectedCountFormat",
        AppxRemovalCategories.SelectMany(category => category.Items).Count(item => item.IsSelected),
        AppxRemovalCategories.SelectMany(category => category.Items).Count());

    public string AppxRemovalProfileSummaryText => ResolveAppxRemovalProfileSummary();

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

    [RelayCommand]
    private void SelectAllAppxRemoval()
    {
        SetAllAppxSelections(true);
    }

    [RelayCommand]
    private void RemoveAllAppxRemoval()
    {
        SetAllAppxSelections(false);
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
        HashSet<string> selectedProfileNames = ResolveAppxRemovalProfileNames(settings, selectedPackageNames);

        isApplyingAppxSelection = true;
        try
        {
            foreach (AppxRemovalItemViewModel item in AppxRemovalCategories.SelectMany(category => category.Items))
            {
                item.IsSelected = hasPersistedSelection
                    ? selectedPackageNames.Contains(item.PackageName)
                    : false;
            }

            foreach (AppxRemovalCategoryViewModel category in AppxRemovalCategories)
            {
                category.IsProfileSelected = selectedProfileNames.Contains(category.DisplayName);
            }
        }
        finally
        {
            isApplyingAppxSelection = false;
        }

        OnPropertyChanged(nameof(AppxRemovalSelectedCountText));
        OnPropertyChanged(nameof(AppxRemovalProfileSummaryText));
    }

    private AppxRemovalSettings BuildAppxRemovalSettings()
    {
        string[] selectedPackageNames = AppxRemovalCategories
            .SelectMany(category => category.Items)
            .Where(item => item.IsSelected)
            .Select(item => item.PackageName)
            .ToArray();

        return IsAppxRemovalEnabled
            ? new AppxRemovalSettings
            {
                IsEnabled = true,
                PackageNames = selectedPackageNames,
                ProfileNames = AppxRemovalCategories
                    .Where(category => category.IsProfileSelected)
                    .Select(category => category.DisplayName)
                    .ToArray()
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
        OnPropertyChanged(nameof(AppxRemovalSelectedCountText));
        OnPropertyChanged(nameof(AppxRemovalProfileSummaryText));
    }

    private void SetAllAppxSelections(bool isSelected)
    {
        SetAppxSelections(_ => isSelected);
    }

    public void ToggleAppxRemovalProfile(AppxRemovalCategoryViewModel selectedCategory)
    {
        bool shouldSelectCategory = !selectedCategory.IsProfileSelected;

        isApplyingAppxSelection = true;
        try
        {
            selectedCategory.IsProfileSelected = shouldSelectCategory;
            foreach (AppxRemovalItemViewModel item in selectedCategory.Items)
            {
                item.IsSelected = shouldSelectCategory;
            }
        }
        finally
        {
            isApplyingAppxSelection = false;
        }

        RefreshAppxRemovalSelectionText();
        SaveState();
    }

    private void SetAppxSelections(Func<AppxRemovalItemViewModel, bool> selector)
    {
        isApplyingAppxSelection = true;
        try
        {
            foreach (AppxRemovalItemViewModel item in AppxRemovalCategories.SelectMany(category => category.Items))
            {
                item.IsSelected = selector(item);
            }

            foreach (AppxRemovalCategoryViewModel category in AppxRemovalCategories)
            {
                category.IsProfileSelected = false;
            }
        }
        finally
        {
            isApplyingAppxSelection = false;
        }

        RefreshAppxRemovalSelectionText();
        SaveState();
    }

    private void RefreshAppxRemovalSelectionText()
    {
        OnPropertyChanged(nameof(AppxRemovalSelectedCountText));
        OnPropertyChanged(nameof(AppxRemovalProfileSummaryText));
    }

    private string ResolveAppxRemovalProfileSummary()
    {
        AppxRemovalCategoryViewModel[] selectedProfiles = AppxRemovalCategories
            .Where(category => category.IsProfileSelected)
            .ToArray();

        bool hasSelectedItems = AppxRemovalCategories
            .SelectMany(category => category.Items)
            .Any(item => item.IsSelected);
        if (!hasSelectedItems)
        {
            return localizationService.GetString("Customization.AppxRemovalProfileNone");
        }

        if (selectedProfiles.Length == 0)
        {
            return localizationService.GetString("Customization.AppxRemovalProfileCustom");
        }

        bool hasOnlySelectedProfilePackages = AppxRemovalCategories.All(category =>
            category.Items.All(item => item.IsSelected == category.IsProfileSelected));
        return hasOnlySelectedProfilePackages
            ? selectedProfiles.Length switch
            {
                1 => selectedProfiles[0].DisplayName,
                _ => localizationService.FormatString("Customization.AppxRemovalProfilesSelectedFormat", selectedProfiles.Length)
            }
            : localizationService.GetString("Customization.AppxRemovalProfileCustom");
    }

    private HashSet<string> ResolveAppxRemovalProfileNames(
        AppxRemovalSettings settings,
        HashSet<string> selectedPackageNames)
    {
        IEnumerable<string> profileNames = settings.ProfileNames ?? InferAppxRemovalProfileNames(selectedPackageNames);
        return profileNames
            .Where(profileName => AppxRemovalCategories.Any(category =>
                string.Equals(category.DisplayName, profileName, StringComparison.OrdinalIgnoreCase)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> InferAppxRemovalProfileNames(HashSet<string> selectedPackageNames)
    {
        if (selectedPackageNames.Count == 0)
        {
            yield break;
        }

        foreach (AppxRemovalCategoryViewModel category in AppxRemovalCategories)
        {
            if (category.Items.Count > 0 && category.Items.All(item => selectedPackageNames.Contains(item.PackageName)))
            {
                yield return category.DisplayName;
            }
        }
    }

    partial void OnIsAppxRemovalEnabledChanged(bool value)
    {
        IsAppxRemovalExpanded = value;
        SaveState();
    }

    private void OnAppxRemovalItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(AppxRemovalItemViewModel.IsSelected), StringComparison.Ordinal))
        {
            return;
        }

        if (!isApplyingState && !isApplyingAppxSelection)
        {
            if (sender is AppxRemovalItemViewModel item)
            {
                AppxRemovalCategoryViewModel? category = AppxRemovalCategories.FirstOrDefault(candidate =>
                    string.Equals(candidate.DisplayName, item.Category, StringComparison.OrdinalIgnoreCase));
                if (category is not null && !category.Items.All(categoryItem => categoryItem.IsSelected))
                {
                    category.IsProfileSelected = false;
                }
            }

            RefreshAppxRemovalSelectionText();
            SaveState();
        }
        else if (!isApplyingAppxSelection)
        {
            RefreshAppxRemovalSelectionText();
        }
    }

}
