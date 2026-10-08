// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using WinUI.TableView;
using TableSortDirection = WinUI.TableView.SortDirection;

namespace Foundry.Views;

/// <summary>
/// Gives a table an initial ascending sort as a regular column sort: the header shows it and the user can
/// replace it by clicking any column header.
/// </summary>
internal static class TableViewDefaultSort
{
    /// <param name="table">The table to sort when it first loads.</param>
    /// <param name="sortMemberPath">The row property to sort by, which may differ from the displayed text.</param>
    /// <param name="columnIndex">The column whose header shows the sort.</param>
    public static void Attach(TableView table, string sortMemberPath, int columnIndex = 0)
    {
        table.Loaded += (_, _) => Apply(table, sortMemberPath, columnIndex);
        table.SizeChanged += OnSizeChanged;
    }

    private static void Apply(TableView table, string sortMemberPath, int columnIndex)
    {
        if (table.IsSorted || columnIndex >= table.Columns.Count) return;
        table.Columns[columnIndex].SortDirection = TableSortDirection.Ascending;
        table.SortDescriptions.Add(new SortDescription(sortMemberPath, TableSortDirection.Ascending));
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
