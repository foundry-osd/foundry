// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.ViewModels;

/// <summary>
/// Represents a string-backed option that can be included in a generated configuration list.
/// </summary>
public sealed partial class SelectableStringOptionViewModel : ObservableObject
{
    public SelectableStringOptionViewModel(string value, string displayName, int sortOrder, bool isSelected, string? description = null)
    {
        Value = value;
        DisplayName = displayName;
        SortOrder = sortOrder;
        IsSelected = isSelected;
        Description = description ?? string.Empty;
    }

    /// <summary>
    /// Gets an optional description shown as a tooltip.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the tooltip content (the description), or <see langword="null"/> when there is no description.
    /// </summary>
    public string? ToolTip => string.IsNullOrWhiteSpace(Description) ? null : Description;

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
