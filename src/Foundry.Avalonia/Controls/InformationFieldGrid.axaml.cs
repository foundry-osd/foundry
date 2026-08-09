// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia;
using Avalonia.Controls;

namespace Foundry.Avalonia.Controls;

public class InformationFieldGrid : ItemsControl
{
    public static readonly StyledProperty<int> ColumnCountProperty =
        AvaloniaProperty.Register<InformationFieldGrid, int>(
            nameof(ColumnCount),
            defaultValue: 1,
            validate: value => value >= 1);

    public int ColumnCount
    {
        get => GetValue(ColumnCountProperty);
        set => SetValue(ColumnCountProperty, value);
    }
}
