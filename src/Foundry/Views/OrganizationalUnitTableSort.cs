// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using WinUI.TableView;
using TableSortDirection = WinUI.TableView.SortDirection;

namespace Foundry.Views;

/// <summary>
/// Sorts an organizational unit table by display name when it first appears, as a regular column sort: the
/// header shows it and the user can replace it by clicking any column header.
/// </summary>
internal static class OrganizationalUnitTableSort
{
    public static void Attach(TableView table)
    {
        table.Loaded += OnLoaded;
        table.SizeChanged += OnSizeChanged;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TableView table || table.IsSorted || table.Columns.FirstOrDefault() is not { } displayNameColumn) return;
        displayNameColumn.SortDirection = TableSortDirection.Ascending;
        table.SortDescriptions.Add(new SortDescription(
            nameof(DomainJoinOrganizationalUnitEntryViewModel.DisplayName), TableSortDirection.Ascending));
    }

    /// <summary>
    /// Shows the sort indicator on headers created after the sort was applied, which happens when the table was
    /// hidden while empty: a header only reads its column's direction when that direction is assigned.
    /// </summary>
    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not TableView table) return;
        foreach (TableViewColumn column in table.Columns)
        {
            if (column.SortDirection is { } direction) column.SortDirection = direction;
        }
    }
}
