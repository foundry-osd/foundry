namespace Foundry.ViewModels;

public sealed partial class AppxRemovalItemViewModel : ObservableObject
{
    public AppxRemovalItemViewModel(string packageName, string displayName, string category, bool defaultSelected)
    {
        PackageName = packageName;
        DisplayName = displayName;
        Category = category;
        DefaultSelected = defaultSelected;
    }

    public string PackageName { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public bool DefaultSelected { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
