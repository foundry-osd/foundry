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
    public partial bool? IsProfileSelected { get; set; }
}
