// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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
    /// Gets or sets the display name shown in the authoring UI; it changes when the UI language changes.
    /// </summary>
    [ObservableProperty]
    public partial string DisplayName { get; set; }

    /// <summary>
    /// Gets the ordering weight used by the authoring UI.
    /// </summary>
    public int SortOrder { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;
}
