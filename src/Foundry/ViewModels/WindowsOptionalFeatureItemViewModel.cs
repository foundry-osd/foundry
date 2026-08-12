// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using Foundry.Core.Models.Configuration;
using Microsoft.UI.Xaml;

namespace Foundry.ViewModels;

public sealed partial class WindowsOptionalFeatureItemViewModel : ObservableObject
{
    private SelectionOption<WindowsOptionalFeatureState>? selectedState;

    public WindowsOptionalFeatureItemViewModel(WindowsOptionalFeatureCatalogEntry catalogEntry, int depth)
    {
        CatalogEntry = catalogEntry;
        Indentation = new Thickness(depth * 20, 0, 0, 0);
    }

    public WindowsOptionalFeatureCatalogEntry CatalogEntry { get; }
    public string Id => CatalogEntry.Id;
    public string FeatureName => CatalogEntry.FeatureName;
    public int SortOrder => CatalogEntry.SortOrder;
    public Thickness Indentation { get; }
    public ObservableCollection<SelectionOption<WindowsOptionalFeatureState>> StateOptions { get; } = [];
    public WindowsOptionalFeatureState State => SelectedState?.Value ?? WindowsOptionalFeatureState.Unchanged;

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DetailText { get; set; } = string.Empty;

    public SelectionOption<WindowsOptionalFeatureState>? SelectedState
    {
        get => selectedState;
        set
        {
            if (value is null || !StateOptions.Any(option => ReferenceEquals(option, value)))
            {
                return;
            }

            if (ReferenceEquals(selectedState, value))
            {
                return;
            }

            selectedState = value;
            OnPropertyChanged();
        }
    }

    public void SetState(WindowsOptionalFeatureState state)
    {
        SelectedState = StateOptions.FirstOrDefault(option => option.Value == state);
    }

    public void RefreshStateOptions(
        string unchangedText,
        string enableText,
        string disableText)
    {
        WindowsOptionalFeatureState state = State;
        StateOptions.Clear();
        StateOptions.Add(new(WindowsOptionalFeatureState.Unchanged, unchangedText));
        StateOptions.Add(new(WindowsOptionalFeatureState.Enable, enableText));
        StateOptions.Add(new(WindowsOptionalFeatureState.Disable, disableText));
        SetState(state);
    }
}
