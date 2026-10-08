// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using WinUI.TableView;

namespace Foundry.Views;

/// <summary>
/// Makes proportional columns span a table whose rows have no selection check box. WinUI.TableView always keeps
/// 32 pixels aside for that check box when it shares out proportional widths, which leaves an empty strip beside
/// the last column in Single and None selection modes. The proportions declared in XAML are therefore applied
/// here as pixel widths each time the table is resized.
/// </summary>
internal static class TableViewFullWidth
{
    // Keeps the columns inside the table edges so a rounding pixel never brings up a horizontal scroll bar.
    private const double EdgeAllowance = 2;

    /// <param name="table">A table without row check boxes whose columns all declare a proportional width.</param>
    public static void Attach(TableView table)
    {
        double[] weights = table.Columns.Select(column => column.Width.IsStar ? column.Width.Value : 0).ToArray();
        if (weights.Length == 0 || weights.Any(weight => weight <= 0)) return;
        table.SizeChanged += (_, _) => Apply(table, weights);
    }

    private static void Apply(TableView table, double[] weights)
    {
        double available = Math.Floor(table.ActualWidth) - EdgeAllowance;
        if (available <= 0 || table.Columns.Count != weights.Length) return;
        double unit = available / weights.Sum();
        double used = 0;
        for (int index = 0; index < weights.Length; index++)
        {
            // The last column takes what rounding left over.
            double width = index == weights.Length - 1 ? available - used : Math.Floor(unit * weights[index]);
            used += width;
            table.Columns[index].Width = new GridLength(width);
        }
    }
}
