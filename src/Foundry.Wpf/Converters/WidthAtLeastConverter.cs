// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Windows.Data;

namespace Foundry.Wpf.Converters;

/// <summary>
/// Returns <see langword="true"/> when the bound width is at least the threshold given as the converter parameter, for
/// example to switch a panel between a side-by-side and a stacked layout. The parameter is a <see cref="double"/> or a
/// number written with the invariant culture (<c>760</c>), so the threshold never depends on the UI culture. A width that
/// is not a number, or a missing or unreadable threshold, gives <see langword="false"/>.
/// </summary>
public sealed class WidthAtLeastConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isWideEnough = value is double width
            && !double.IsNaN(width)
            && TryReadThreshold(parameter, out double threshold)
            && width >= threshold;
        return isWideEnough;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static bool TryReadThreshold(object? parameter, out double threshold)
    {
        switch (parameter)
        {
            case double number when !double.IsNaN(number):
                threshold = number;
                return true;
            case string text:
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out threshold)
                    && !double.IsNaN(threshold);
            default:
                threshold = 0;
                return false;
        }
    }
}
