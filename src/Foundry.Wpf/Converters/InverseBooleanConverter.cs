// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Foundry.Wpf.Converters;

/// <summary>
/// Maps a <see cref="bool"/> to <see cref="Visibility"/> with inverted logic: <see langword="true"/> collapses
/// the element, <see langword="false"/> shows it. A non-boolean value is treated like <see langword="false"/>
/// and therefore shows the element. The name describes the input inversion; the result is always a <see cref="Visibility"/>.
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isVisible = value is bool boolValue && !boolValue;
        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Reverses <see cref="Convert"/>: <see cref="Visibility.Visible"/> gives <see langword="false"/>, any other
    /// visibility gives <see langword="true"/>; other inputs are ignored via <see cref="Binding.DoNothing"/>.
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility != Visibility.Visible;
        }

        return Binding.DoNothing;
    }
}
