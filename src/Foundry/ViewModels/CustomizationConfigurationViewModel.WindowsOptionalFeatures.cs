// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration;
using Microsoft.UI.Xaml;

namespace Foundry.ViewModels;

public sealed partial class CustomizationConfigurationViewModel
{
    private const int WindowsOptionalFeatureBulkConfirmationThreshold = 10;

    private readonly Dictionary<string, WindowsOptionalFeatureItemViewModel> windowsOptionalFeatureItemsById =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> windowsOptionalFeatureBulkActionIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> expandedWindowsOptionalFeatureCategories = new(StringComparer.Ordinal);
    private readonly HashSet<string> expandedWindowsOptionalFeatureIds = new(StringComparer.OrdinalIgnoreCase);
    private bool isApplyingWindowsOptionalFeatureSelection;

    public ObservableCollection<WindowsOptionalFeatureCategoryViewModel> WindowsOptionalFeatureCategories { get; } = [];
    public ObservableCollection<WindowsOptionalFeatureTreeNodeViewModel> VisibleWindowsOptionalFeatureTreeRoots { get; } = [];
    public bool IsWindowsOptionalFeatureOptionsEnabled => IsWindowsOptionalFeaturesEnabled;
    public Visibility WindowsOptionalFeatureEmptySearchVisibility => VisibleWindowsOptionalFeatureTreeRoots.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

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
    public partial string WindowsOptionalFeatureEnableAllText { get; set; }

    [ObservableProperty]
    public partial string WindowsOptionalFeatureDisableAllText { get; set; }

    [ObservableProperty]
    public partial string WindowsOptionalFeatureResetAllText { get; set; }

    [ObservableProperty]
    public partial string WindowsOptionalFeatureSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWindowsOptionalFeatureOptionsEnabled))]
    public partial bool IsWindowsOptionalFeaturesEnabled { get; set; }

    private void InitializeWindowsOptionalFeatureCatalog()
    {
        foreach (WindowsOptionalFeatureCatalogEntry entry in WindowsOptionalFeatureCatalog.Entries)
        {
            var item = new WindowsOptionalFeatureItemViewModel(entry);
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
        if (!initializedCatalogs.Contains(CustomizationCatalog.WindowsOptionalFeatures))
        {
            return configurationStateService.Current.Customization.WindowsOptionalFeatures;
        }

        return WindowsOptionalFeatureSettingsNormalizer.Normalize(new WindowsOptionalFeatureSettings
        {
            IsEnabled = IsWindowsOptionalFeaturesEnabled,
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
        WindowsOptionalFeaturesExplanation = localizationService.GetString("Customization.WindowsOptionalFeaturesExplanation");
        WindowsOptionalFeatureSearchLabel = localizationService.GetString("Customization.WindowsOptionalFeaturesSearchLabel");
        WindowsOptionalFeatureSearchPlaceholder = localizationService.GetString("Customization.WindowsOptionalFeaturesSearchPlaceholder");
        WindowsOptionalFeatureEmptySearchText = localizationService.GetString("Customization.WindowsOptionalFeaturesEmptySearch");
        WindowsOptionalFeatureEnableAllText = localizationService.GetString("Customization.WindowsOptionalFeatures.Action.EnableAll");
        WindowsOptionalFeatureDisableAllText = localizationService.GetString("Customization.WindowsOptionalFeatures.Action.DisableAll");
        WindowsOptionalFeatureResetAllText = localizationService.GetString("Customization.WindowsOptionalFeatures.Action.ResetAll");

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
                    item.RefreshStateText(unchangedText, enableText, disableText);
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
        ApplyWindowsOptionalFeatureSelection(
            category.AllItems
                .Where(item => windowsOptionalFeatureBulkActionIds.Contains(item.Id))
                .Select(item => item.Id),
            state);
    }

    private void ApplyWindowsOptionalFeatureItemState(
        WindowsOptionalFeatureItemViewModel item,
        WindowsOptionalFeatureState state)
        => ApplyWindowsOptionalFeatureSelection((string[])[item.Id], state);

    private void ApplyWindowsOptionalFeatureSelection(
        IEnumerable<string> featureIds,
        WindowsOptionalFeatureState state)
    {
        bool? enable = state switch
        {
            WindowsOptionalFeatureState.Enable => true,
            WindowsOptionalFeatureState.Disable => false,
            _ => null
        };
        WindowsOptionalFeatureSettings updated = WindowsOptionalFeatureSelectionUpdater.ApplySubtreeStates(
            BuildWindowsOptionalFeatureSettings(),
            featureIds,
            enable);

        ApplyWindowsOptionalFeatureState(updated);
        SaveWindowsOptionalFeatureState();
    }

    [RelayCommand]
    private Task EnableAllVisibleWindowsOptionalFeaturesAsync()
        => ApplyVisibleWindowsOptionalFeatureStateAsync(WindowsOptionalFeatureState.Enable);

    [RelayCommand]
    private Task DisableAllVisibleWindowsOptionalFeaturesAsync()
        => ApplyVisibleWindowsOptionalFeatureStateAsync(WindowsOptionalFeatureState.Disable);

    [RelayCommand]
    private Task ResetAllVisibleWindowsOptionalFeaturesAsync()
        => ApplyVisibleWindowsOptionalFeatureStateAsync(WindowsOptionalFeatureState.Unchanged);

    private async Task ApplyVisibleWindowsOptionalFeatureStateAsync(WindowsOptionalFeatureState state)
    {
        WindowsOptionalFeatureItemViewModel[] targets = windowsOptionalFeatureBulkActionIds
            .Select(id => windowsOptionalFeatureItemsById[id])
            .OrderBy(item => item.SortOrder)
            .ToArray();
        if (targets.Length == 0)
        {
            return;
        }

        IReadOnlyList<string> affectedIds = WindowsOptionalFeatureSelectionUpdater.GetAffectedFeatureIds(
            targets.Select(item => item.Id));
        if (affectedIds.Count >= WindowsOptionalFeatureBulkConfirmationThreshold &&
            !await dialogService.ConfirmAsync(new ConfirmationDialogRequest(
                localizationService.GetString("Customization.WindowsOptionalFeatures.BulkConfirmationTitle"),
                localizationService.FormatString(
                    "Customization.WindowsOptionalFeatures.BulkConfirmationMessageFormat",
                    affectedIds.Count),
                localizationService.GetString("Customization.WindowsOptionalFeatures.BulkConfirmationPrimary"),
                localizationService.GetString("Common.Cancel"))))
        {
            return;
        }

        ApplyWindowsOptionalFeatureSelection(affectedIds, state);
    }

    private void RebuildVisibleWindowsOptionalFeatures()
    {
        string searchText = WindowsOptionalFeatureSearchText.Trim();
        HashSet<string> visibleIds = new(StringComparer.OrdinalIgnoreCase);
        windowsOptionalFeatureBulkActionIds.Clear();
        if (searchText.Length == 0)
        {
            visibleIds.UnionWith(windowsOptionalFeatureItemsById.Keys);
            windowsOptionalFeatureBulkActionIds.UnionWith(windowsOptionalFeatureItemsById.Keys);
        }
        else
        {
            foreach (WindowsOptionalFeatureItemViewModel item in windowsOptionalFeatureItemsById.Values.Where(item =>
                         item.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                         item.FeatureName.Contains(searchText, StringComparison.OrdinalIgnoreCase)))
            {
                visibleIds.Add(item.Id);
                windowsOptionalFeatureBulkActionIds.Add(item.Id);
                visibleIds.UnionWith(WindowsOptionalFeatureCatalog.GetAncestors(item.Id).Select(ancestor => ancestor.Id));
            }
        }

        VisibleWindowsOptionalFeatureTreeRoots.Clear();
        foreach (WindowsOptionalFeatureCategoryViewModel category in WindowsOptionalFeatureCategories)
        {
            category.VisibleItems.Clear();
            foreach (WindowsOptionalFeatureItemViewModel item in category.AllItems.Where(item => visibleIds.Contains(item.Id)))
            {
                category.VisibleItems.Add(item);
            }

            if (category.VisibleItems.Count > 0)
            {
                IEnumerable<WindowsOptionalFeatureTreeNodeViewModel> roots = category.AllItems
                    .Where(item => item.CatalogEntry.ParentId is null && visibleIds.Contains(item.Id))
                    .Select(item => BuildWindowsOptionalFeatureTreeNode(item, visibleIds, searchText.Length > 0));
                bool isSearching = searchText.Length > 0;
                var categoryNode = new WindowsOptionalFeatureTreeNodeViewModel(
                    category,
                    roots,
                    isSearching || expandedWindowsOptionalFeatureCategories.Contains(category.ResourceKey),
                    isSearching ? null : UpdateExpandedWindowsOptionalFeatureCategory);
                VisibleWindowsOptionalFeatureTreeRoots.Add(categoryNode);
            }
        }

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

        foreach (WindowsOptionalFeatureTreeNodeViewModel root in VisibleWindowsOptionalFeatureTreeRoots)
        {
            root.Refresh();
        }
    }

    private WindowsOptionalFeatureTreeNodeViewModel BuildWindowsOptionalFeatureTreeNode(
        WindowsOptionalFeatureItemViewModel item,
        IReadOnlySet<string> visibleIds,
        bool expand)
    {
        IEnumerable<WindowsOptionalFeatureTreeNodeViewModel> children = WindowsOptionalFeatureCatalog
            .GetChildren(item.Id)
            .Where(entry => visibleIds.Contains(entry.Id))
            .Select(entry => BuildWindowsOptionalFeatureTreeNode(windowsOptionalFeatureItemsById[entry.Id], visibleIds, expand));
        return new WindowsOptionalFeatureTreeNodeViewModel(
            item,
            children,
            expand || expandedWindowsOptionalFeatureIds.Contains(item.Id),
            expand ? null : UpdateExpandedWindowsOptionalFeature,
            ApplyWindowsOptionalFeatureItemState);
    }

    private void UpdateExpandedWindowsOptionalFeatureCategory(string resourceKey, bool isExpanded)
    {
        UpdateExpansionState(expandedWindowsOptionalFeatureCategories, resourceKey, isExpanded);
    }

    private void UpdateExpandedWindowsOptionalFeature(string id, bool isExpanded)
    {
        UpdateExpansionState(expandedWindowsOptionalFeatureIds, id, isExpanded);
    }

    private static void UpdateExpansionState(HashSet<string> expandedKeys, string key, bool isExpanded)
    {
        if (isExpanded)
        {
            expandedKeys.Add(key);
        }
        else
        {
            expandedKeys.Remove(key);
        }
    }

    partial void OnIsWindowsOptionalFeaturesEnabledChanged(bool value)
    {
        if (isApplyingState || isApplyingWindowsOptionalFeatureSelection)
        {
            return;
        }

        SaveWindowsOptionalFeatureState();
    }

    partial void OnWindowsOptionalFeatureSearchTextChanged(string value)
    {
        RebuildVisibleWindowsOptionalFeatures();
    }

}
