// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;

namespace Foundry.ViewModels;

public sealed partial class WindowsOptionalFeatureCategoryViewModel : ObservableObject
{
    private readonly Action<WindowsOptionalFeatureCategoryViewModel, WindowsOptionalFeatureState> applyState;

    public WindowsOptionalFeatureCategoryViewModel(
        string resourceKey,
        IEnumerable<WindowsOptionalFeatureItemViewModel> items,
        Action<WindowsOptionalFeatureCategoryViewModel, WindowsOptionalFeatureState> applyState)
    {
        ResourceKey = resourceKey;
        AllItems = items.OrderBy(item => item.SortOrder).ToArray();
        VisibleItems = new ObservableCollection<WindowsOptionalFeatureItemViewModel>(AllItems);
        this.applyState = applyState;
    }

    public string ResourceKey { get; }
    public IReadOnlyList<WindowsOptionalFeatureItemViewModel> AllItems { get; }
    public ObservableCollection<WindowsOptionalFeatureItemViewModel> VisibleItems { get; }

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SummaryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EnableActionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DisableActionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ClearActionText { get; set; } = string.Empty;

    [RelayCommand]
    private void EnableVisible() => applyState(this, WindowsOptionalFeatureState.Enable);

    [RelayCommand]
    private void DisableVisible() => applyState(this, WindowsOptionalFeatureState.Disable);

    [RelayCommand]
    private void ClearVisible() => applyState(this, WindowsOptionalFeatureState.Unchanged);
}
