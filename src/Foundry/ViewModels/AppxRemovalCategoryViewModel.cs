using System.Collections.ObjectModel;

namespace Foundry.ViewModels;

public sealed class AppxRemovalCategoryViewModel
{
    public AppxRemovalCategoryViewModel(string displayName, IEnumerable<AppxRemovalItemViewModel> items)
    {
        DisplayName = displayName;
        Items = new ObservableCollection<AppxRemovalItemViewModel>(items);
    }

    public string DisplayName { get; }
    public ObservableCollection<AppxRemovalItemViewModel> Items { get; }
}
