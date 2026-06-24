// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;

namespace Foundry.ViewModels;

public sealed partial class AppxRemovalCategoryViewModel : ObservableObject
{
    public AppxRemovalCategoryViewModel(string displayName, IEnumerable<AppxRemovalItemViewModel> items)
    {
        DisplayName = displayName;
        Items = new ObservableCollection<AppxRemovalItemViewModel>(items);
    }

    public string DisplayName { get; }
    public ObservableCollection<AppxRemovalItemViewModel> Items { get; }

    [ObservableProperty]
    public partial bool IsProfileSelected { get; set; }
}
