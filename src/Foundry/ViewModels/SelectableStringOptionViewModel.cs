namespace Foundry.ViewModels;

/// <summary>
/// Represents a string-backed option that can be included in a generated configuration list.
/// </summary>
public sealed partial class SelectableStringOptionViewModel : ObservableObject
{
    public SelectableStringOptionViewModel(string value, string displayName, int sortOrder, bool isSelected)
    {
        Value = value;
        DisplayName = displayName;
        SortOrder = sortOrder;
        IsSelected = isSelected;
    }

    /// <summary>
    /// Gets the invariant value persisted in configuration.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets the display name shown in the authoring UI.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the ordering weight used by the authoring UI.
    /// </summary>
    public int SortOrder { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
