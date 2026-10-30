// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Wpf.Converters;

namespace Foundry.Wpf.Tests.Converters;

public sealed class WidthAtLeastConverterTests
{
    public static TheoryData<object?, object?, bool> Cases => new()
    {
        { 759.9, "760", false },
        { 760.0, "760", true },
        { 1200.0, "760", true },
        { double.NaN, "760", false },
        { "800", "760", false },
        { 800.0, "abc", false },
        { 800.0, null, false },
        { 800.0, 760.0, true }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Convert_ComparesWidthWithThreshold(object? width, object? threshold, bool expected)
    {
        Assert.Equal(expected, new WidthAtLeastConverter().Convert(width!, typeof(bool), threshold!, CultureInfo.GetCultureInfo("fr-FR")));
    }
}
