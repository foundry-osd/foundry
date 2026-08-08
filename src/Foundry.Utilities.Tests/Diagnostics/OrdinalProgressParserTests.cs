// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Diagnostics;

namespace Foundry.Utilities.Tests.Diagnostics;

public sealed class OrdinalProgressParserTests
{
    [Theory]
    [InlineData("1 of 4", 25d)]
    [InlineData("3 sur 4", 75d)]
    [InlineData("2 OF 4", 50d)]
    [InlineData("2 SUR 4", 50d)]
    [InlineData("5 of 4", 100d)]
    public void TryParse_WithOrdinalProgress_ReturnsClampedPercentage(string line, double expected)
    {
        bool parsed = OrdinalProgressParser.TryParse(line, out double percent);

        Assert.True(parsed);
        Assert.Equal(expected, percent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ordinary diagnostic text")]
    [InlineData("0 of 4")]
    [InlineData("1 of 0")]
    [InlineData("-1 of 10")]
    [InlineData("1 of -10")]
    [InlineData("999999999999999999999 of 10")]
    public void TryParse_WithInvalidOrdinalProgress_ReturnsFalse(string? line)
    {
        bool parsed = OrdinalProgressParser.TryParse(line, out double percent);

        Assert.False(parsed);
        Assert.Equal(0d, percent);
    }
}
