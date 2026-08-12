// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Utilities.Collections;
using Microsoft.UI.Xaml;

namespace Foundry.ViewModels;

public sealed partial class CustomizationConfigurationViewModel
{
    private readonly Dictionary<string, WindowsOptionalFeatureItemViewModel> windowsOptionalFeatureItemsById =
        new(StringComparer.OrdinalIgnoreCase);
    private bool isApplyingWindowsOptionalFeatureSelection;

    public ObservableCollection<WindowsOptionalFeatureCategoryViewModel> WindowsOptionalFeatureCategories { get; } = [];
    public ObservableCollection<WindowsOptionalFeatureCategoryViewModel> VisibleWindowsOptionalFeatureCategories { get; } = [];
    public bool IsWindowsOptionalFeatureOptionsEnabled => IsWindowsOptionalFeaturesEnabled;
    public Visibility WindowsOptionalFeatureEmptySearchVisibility => VisibleWindowsOptionalFeatureCategories.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    [ObservableProperty]
    public partial string WindowsOptionalFeaturesHeader { get; set; }

    [ObservableProperty]
    public partial string WindowsOptionalFeaturesDescription { get; set; }

    [ObservableProperty]
    public partial string WindowsOptionalFeaturesEnableText { get; set; }

    [ObservableProperty]
    public partial string WindowsOptionalFeaturesExplanation { get; set; }

    [ObservableProperty]
    public partial string WindowsOptionalFeatureSearchLabel { get; set; }

    [ObservableProperty]
    public partial string WindowsOptionalFeatureSearchPlaceholder { get; set; }

    [ObservableProperty]
    public partial string WindowsOptionalFeatureEmptySearchText { get; set; }

    [ObservableProperty]
    public partial string WindowsOptionalFeatureConfiguredCountText { get; set; }

    [ObservableProperty]
    public partial string WindowsOptionalFeatureSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsWindowsOptionalFeaturesExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWindowsOptionalFeatureOptionsEnabled))]
    public partial bool IsWindowsOptionalFeaturesEnabled { get; set; }

    private void InitializeWindowsOptionalFeatureCatalog()
    {
        foreach (WindowsOptionalFeatureCatalogEntry entry in WindowsOptionalFeatureCatalog.Entries)
        {
            var item = new WindowsOptionalFeatureItemViewModel(entry, WindowsOptionalFeatureCatalog.GetDepth(entry.Id));
            item.PropertyChanged += OnWindowsOptionalFeatureItemPropertyChanged;
            windowsOptionalFeatureItemsById.Add(item.Id, item);
        }

        foreach (IGrouping<string, WindowsOptionalFeatureItemViewModel> group in windowsOptionalFeatureItemsById.Values
                     .OrderBy(item => item.SortOrder)
                     .GroupBy(item => item.CatalogEntry.CategoryResourceKey))
        {
            WindowsOptionalFeatureCategories.Add(new WindowsOptionalFeatureCategoryViewModel(group.Key, group, ApplyCategoryState));
        }

        RebuildVisibleWindowsOptionalFeatures();
    }

    private void ApplyWindowsOptionalFeatureState(WindowsOptionalFeatureSettings settings)
    {
        WindowsOptionalFeatureSettings normalized = WindowsOptionalFeatureSettingsNormalizer.Normalize(settings);
        HashSet<string> enabledIds = normalized.EnabledFeatureIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> disabledIds = normalized.DisabledFeatureIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        isApplyingWindowsOptionalFeatureSelection = true;
        try
        {
            IsWindowsOptionalFeaturesEnabled = normalized.IsEnabled;
            IsWindowsOptionalFeaturesExpanded = normalized.IsEnabled;
            foreach (WindowsOptionalFeatureItemViewModel item in windowsOptionalFeatureItemsById.Values)
            {
                item.SetState(enabledIds.Contains(item.Id)
                    ? WindowsOptionalFeatureState.Enable
                    : disabledIds.Contains(item.Id)
                        ? WindowsOptionalFeatureState.Disable
                        : WindowsOptionalFeatureState.Unchanged);
            }
        }
        finally
        {
            isApplyingWindowsOptionalFeatureSelection = false;
        }

        RefreshWindowsOptionalFeatureCompatibility();
        RefreshWindowsOptionalFeatureSummaries();
    }

    private WindowsOptionalFeatureSettings BuildWindowsOptionalFeatureSettings()
    {
        if (!IsWindowsOptionalFeaturesEnabled)
        {
            return new WindowsOptionalFeatureSettings();
        }

        return WindowsOptionalFeatureSettingsNormalizer.Normalize(new WindowsOptionalFeatureSettings
        {
            IsEnabled = true,
            EnabledFeatureIds = windowsOptionalFeatureItemsById.Values
                .Where(item => item.State == WindowsOptionalFeatureState.Enable)
                .OrderBy(item => item.SortOrder)
                .Select(item => item.Id)
                .ToArray(),
            DisabledFeatureIds = windowsOptionalFeatureItemsById.Values
                .Where(item => item.State == WindowsOptionalFeatureState.Disable)
                .OrderBy(item => item.SortOrder)
                .Select(item => item.Id)
                .ToArray()
        });
    }

    private void RefreshWindowsOptionalFeatureLocalizedText()
    {
        WindowsOptionalFeaturesHeader = localizationService.GetString("Customization.WindowsOptionalFeaturesHeader");
        WindowsOptionalFeaturesDescription = localizationService.GetString("Customization.WindowsOptionalFeaturesDescription");
        WindowsOptionalFeaturesEnableText = localizationService.GetString("Customization.WindowsOptionalFeaturesEnableLabel");
        WindowsOptionalFeaturesExplanation = localizationService.GetString("Customization.WindowsOptionalFeaturesExplanation");
        WindowsOptionalFeatureSearchLabel = localizationService.GetString("Customization.WindowsOptionalFeaturesSearchLabel");
        WindowsOptionalFeatureSearchPlaceholder = localizationService.GetString("Customization.WindowsOptionalFeaturesSearchPlaceholder");
        WindowsOptionalFeatureEmptySearchText = localizationService.GetString("Customization.WindowsOptionalFeaturesEmptySearch");

        string unchangedText = localizationService.GetString("Customization.WindowsOptionalFeatures.State.Unchanged");
        string enableText = localizationService.GetString("Customization.WindowsOptionalFeatures.State.Enable");
        string disableText = localizationService.GetString("Customization.WindowsOptionalFeatures.State.Disable");
        string enableActionText = localizationService.GetString("Customization.WindowsOptionalFeatures.Action.Enable");
        string disableActionText = localizationService.GetString("Customization.WindowsOptionalFeatures.Action.Disable");
        string clearActionText = localizationService.GetString("Customization.WindowsOptionalFeatures.Action.Clear");

        isApplyingWindowsOptionalFeatureSelection = true;
        try
        {
            foreach (WindowsOptionalFeatureCategoryViewModel category in WindowsOptionalFeatureCategories)
            {
                category.DisplayName = localizationService.GetString(category.ResourceKey);
                category.EnableActionText = enableActionText;
                category.DisableActionText = disableActionText;
                category.ClearActionText = clearActionText;
                foreach (WindowsOptionalFeatureItemViewModel item in category.AllItems)
                {
                    item.DisplayName = localizationService.GetString(item.CatalogEntry.DisplayNameResourceKey);
                    item.RefreshStateOptions(unchangedText, enableText, disableText);
                }
            }
        }
        finally
        {
            isApplyingWindowsOptionalFeatureSelection = false;
        }

        RefreshWindowsOptionalFeatureCompatibility();
        RebuildVisibleWindowsOptionalFeatures();
        RefreshWindowsOptionalFeatureSummaries();
    }

    private void RefreshWindowsOptionalFeatureCompatibility()
    {
        if (WindowsOptionalFeatureCategories.Count == 0)
        {
            return;
        }

        OperatingSystemSelectionSettings operatingSystemSelection = BuildOperatingSystemSelectionSettings();
        IEnumerable<string> editions = operatingSystemSelection.IsEnabled
            ? operatingSystemSelection.AllowedEditions
            : [];
        IEnumerable<string> releases = operatingSystemSelection.IsEnabled
            ? operatingSystemSelection.AllowedReleaseIds
            : [];

        foreach (WindowsOptionalFeatureItemViewModel item in windowsOptionalFeatureItemsById.Values)
        {
            WindowsOptionalFeatureCompatibility compatibility = WindowsOptionalFeatureCompatibilityEvaluator.Evaluate(
                item.Id,
                editions,
                releases,
                configurationStateService.Current.General.Architecture);
            WindowsOptionalFeatureCatalogEntry effectiveEntry =
                WindowsOptionalFeatureCatalog.GetEffectiveEntry(item.Id) ?? item.CatalogEntry;
            List<string> details =
            [
                localizationService.FormatString(
                    "Customization.WindowsOptionalFeatures.DetailFormat",
                    item.FeatureName,
                    localizationService.GetString($"Customization.WindowsOptionalFeatures.Compatibility.{compatibility}"))
            ];

            if (effectiveEntry.RequiresSetupMediaSxs)
            {
                details.Add(localizationService.GetString("Customization.WindowsOptionalFeatures.Warning.MatchingSource"));
            }

            if (effectiveEntry.WarningResourceKey is not null)
            {
                details.Add(localizationService.GetString(effectiveEntry.WarningResourceKey));
            }

            if (string.Equals(item.FeatureName, "Microsoft-Windows-Subsystem-Linux", StringComparison.OrdinalIgnoreCase))
            {
                details.Add(localizationService.GetString("Customization.WindowsOptionalFeatures.Warning.WslComponentOnly"));
            }

            if (string.Equals(item.FeatureName, "Recall", StringComparison.OrdinalIgnoreCase))
            {
                details.Add(localizationService.GetString("Customization.WindowsOptionalFeatures.Warning.RecallPolicyIndependent"));
            }

            item.DetailText = string.Join(Environment.NewLine, details);
        }
    }

    private void ApplyCategoryState(
        WindowsOptionalFeatureCategoryViewModel category,
        WindowsOptionalFeatureState state)
    {
        isApplyingWindowsOptionalFeatureSelection = true;
        try
        {
            foreach (WindowsOptionalFeatureItemViewModel item in category.VisibleItems)
            {
                item.SetState(state);
            }

            NormalizeWindowsOptionalFeatureAncestorConflicts();
        }
        finally
        {
            isApplyingWindowsOptionalFeatureSelection = false;
        }

        RefreshWindowsOptionalFeatureSummaries();
        SaveState();
    }

    private void NormalizeWindowsOptionalFeatureAncestorConflicts()
    {
        foreach (WindowsOptionalFeatureItemViewModel item in windowsOptionalFeatureItemsById.Values
                     .Where(item => item.State == WindowsOptionalFeatureState.Enable))
        {
            if (WindowsOptionalFeatureCatalog.GetAncestors(item.Id)
                .Any(ancestor => windowsOptionalFeatureItemsById[ancestor.Id].State == WindowsOptionalFeatureState.Disable))
            {
                item.SetState(WindowsOptionalFeatureState.Unchanged);
            }
        }
    }

    private void RebuildVisibleWindowsOptionalFeatures()
    {
        string searchText = WindowsOptionalFeatureSearchText.Trim();
        HashSet<string> visibleIds = new(StringComparer.OrdinalIgnoreCase);
        if (searchText.Length == 0)
        {
            visibleIds.UnionWith(windowsOptionalFeatureItemsById.Keys);
        }
        else
        {
            foreach (WindowsOptionalFeatureItemViewModel item in windowsOptionalFeatureItemsById.Values.Where(item =>
                         item.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                         item.FeatureName.Contains(searchText, StringComparison.OrdinalIgnoreCase)))
            {
                visibleIds.Add(item.Id);
                visibleIds.UnionWith(WindowsOptionalFeatureCatalog.GetAncestors(item.Id).Select(ancestor => ancestor.Id));
            }
        }

        List<WindowsOptionalFeatureCategoryViewModel> visibleCategories = [];
        foreach (WindowsOptionalFeatureCategoryViewModel category in WindowsOptionalFeatureCategories)
        {
            WindowsOptionalFeatureItemViewModel[] visibleItems = category.AllItems
                .Where(item => visibleIds.Contains(item.Id))
                .ToArray();
            category.VisibleItems.SynchronizeReferences(visibleItems);

            if (category.VisibleItems.Count > 0)
            {
                category.IsExpanded = searchText.Length > 0 || category.IsExpanded;
                visibleCategories.Add(category);
            }
        }

        VisibleWindowsOptionalFeatureCategories.SynchronizeReferences(visibleCategories);
        OnPropertyChanged(nameof(WindowsOptionalFeatureEmptySearchVisibility));
    }

    private void RefreshWindowsOptionalFeatureSummaries()
    {
        int enableCount = windowsOptionalFeatureItemsById.Values.Count(item => item.State == WindowsOptionalFeatureState.Enable);
        int disableCount = windowsOptionalFeatureItemsById.Values.Count(item => item.State == WindowsOptionalFeatureState.Disable);
        WindowsOptionalFeatureConfiguredCountText = localizationService.FormatString(
            "Customization.WindowsOptionalFeatures.SummaryFormat",
            enableCount + disableCount,
            enableCount,
            disableCount);

        foreach (WindowsOptionalFeatureCategoryViewModel category in WindowsOptionalFeatureCategories)
        {
            int categoryEnableCount = category.AllItems.Count(item => item.State == WindowsOptionalFeatureState.Enable);
            int categoryDisableCount = category.AllItems.Count(item => item.State == WindowsOptionalFeatureState.Disable);
            category.SummaryText = localizationService.FormatString(
                "Customization.WindowsOptionalFeatures.SummaryFormat",
                categoryEnableCount + categoryDisableCount,
                categoryEnableCount,
                categoryDisableCount);
        }
    }

    partial void OnIsWindowsOptionalFeaturesEnabledChanged(bool value)
    {
        IsWindowsOptionalFeaturesExpanded = value;
        if (isApplyingState || isApplyingWindowsOptionalFeatureSelection)
        {
            return;
        }

        if (!value)
        {
            isApplyingWindowsOptionalFeatureSelection = true;
            try
            {
                foreach (WindowsOptionalFeatureItemViewModel item in windowsOptionalFeatureItemsById.Values)
                {
                    item.SetState(WindowsOptionalFeatureState.Unchanged);
                }
            }
            finally
            {
                isApplyingWindowsOptionalFeatureSelection = false;
            }

            RefreshWindowsOptionalFeatureSummaries();
        }

        SaveState();
    }

    partial void OnWindowsOptionalFeatureSearchTextChanged(string value)
    {
        RebuildVisibleWindowsOptionalFeatures();
    }

    private void OnWindowsOptionalFeatureItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(WindowsOptionalFeatureItemViewModel.SelectedState), StringComparison.Ordinal) ||
            sender is not WindowsOptionalFeatureItemViewModel item ||
            isApplyingWindowsOptionalFeatureSelection)
        {
            return;
        }

        isApplyingWindowsOptionalFeatureSelection = true;
        try
        {
            if (item.State == WindowsOptionalFeatureState.Disable)
            {
                foreach (WindowsOptionalFeatureCatalogEntry descendant in WindowsOptionalFeatureCatalog.Entries.Where(entry =>
                             WindowsOptionalFeatureCatalog.GetAncestors(entry.Id).Any(ancestor =>
                                 string.Equals(ancestor.Id, item.Id, StringComparison.OrdinalIgnoreCase))))
                {
                    if (windowsOptionalFeatureItemsById[descendant.Id].State == WindowsOptionalFeatureState.Enable)
                    {
                        windowsOptionalFeatureItemsById[descendant.Id].SetState(WindowsOptionalFeatureState.Unchanged);
                    }
                }
            }

            NormalizeWindowsOptionalFeatureAncestorConflicts();
        }
        finally
        {
            isApplyingWindowsOptionalFeatureSelection = false;
        }

        RefreshWindowsOptionalFeatureSummaries();
        SaveState();
    }
}
