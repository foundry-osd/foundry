namespace Foundry.ViewModels;

public sealed partial class LocalizationLanguageOptionViewModel : ObservableObject
{
    public LocalizationLanguageOptionViewModel(string code, string displayName, int sortOrder, bool isSelected)
    {
        Code = code;
        DisplayName = displayName;
        SortOrder = sortOrder;
        IsSelected = isSelected;
    }

    public string Code { get; }
    public string DisplayName { get; }
    public int SortOrder { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
