// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Foundry.ViewModels;

public sealed partial class WindowsOptionalFeatureTreeNodeViewModel : ObservableObject
{
    private readonly WindowsOptionalFeatureCategoryViewModel? category;
    private readonly WindowsOptionalFeatureItemViewModel? item;
    private readonly string expansionKey;
    private readonly Action<string, bool>? expansionChanged;
    private bool trackExpansionChanges;

    public WindowsOptionalFeatureTreeNodeViewModel(
        WindowsOptionalFeatureCategoryViewModel category,
        IEnumerable<WindowsOptionalFeatureTreeNodeViewModel> children,
        bool isExpanded,
        Action<string, bool>? expansionChanged)
    {
        this.category = category;
        expansionKey = category.ResourceKey;
        this.expansionChanged = expansionChanged;
        Children = new ObservableCollection<WindowsOptionalFeatureTreeNodeViewModel>(children);
        EnableCommand = category.EnableVisibleCommand;
        DisableCommand = category.DisableVisibleCommand;
        ClearCommand = category.ClearVisibleCommand;
        IsExpanded = isExpanded;
        trackExpansionChanges = true;
    }

    public WindowsOptionalFeatureTreeNodeViewModel(
        WindowsOptionalFeatureItemViewModel item,
        IEnumerable<WindowsOptionalFeatureTreeNodeViewModel> children,
        bool isExpanded,
        Action<string, bool>? expansionChanged)
    {
        this.item = item;
        expansionKey = item.Id;
        this.expansionChanged = expansionChanged;
        Children = new ObservableCollection<WindowsOptionalFeatureTreeNodeViewModel>(children);
        EnableCommand = new RelayCommand(() => item.SetState(WindowsOptionalFeatureState.Enable));
        DisableCommand = new RelayCommand(() => item.SetState(WindowsOptionalFeatureState.Disable));
        ClearCommand = new RelayCommand(() => item.SetState(WindowsOptionalFeatureState.Unchanged));
        IsExpanded = isExpanded;
        trackExpansionChanges = true;
    }

    public ObservableCollection<WindowsOptionalFeatureTreeNodeViewModel> Children { get; }
    public ICommand EnableCommand { get; }
    public ICommand DisableCommand { get; }
    public ICommand ClearCommand { get; }
    public string DisplayName => category?.DisplayName ?? item!.DisplayName;
    public string DetailText => category?.SummaryText ?? item!.DetailText;
    public string StateText => category?.SummaryText ?? item!.StateText;
    public string EnableActionText => category?.EnableActionText ?? item!.EnableActionText;
    public string DisableActionText => category?.DisableActionText ?? item!.DisableActionText;
    public string ClearActionText => category?.ClearActionText ?? item!.ClearActionText;
    public string IconGlyph => category is null ? "\uE9D2" : "\uE8B7";

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        if (trackExpansionChanges)
        {
            expansionChanged?.Invoke(expansionKey, value);
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(EnableActionText));
        OnPropertyChanged(nameof(DisableActionText));
        OnPropertyChanged(nameof(ClearActionText));
        foreach (WindowsOptionalFeatureTreeNodeViewModel child in Children)
        {
            child.Refresh();
        }
    }
}
