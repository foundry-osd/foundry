// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Diagnostics;

namespace Foundry.Utilities.Tests.Diagnostics;

public sealed class PercentageProgressParserTests
{
    [Theory]
    [InlineData("50%", 50d)]
    [InlineData("progress 50.5% complete", 50.5d)]
    [InlineData("progress 50,5% complete", 50.5d)]
    [InlineData("125%", 100d)]
    [InlineData("0%", 0d)]
    public void TryParse_WithPercentage_ReturnsClampedInvariantValue(string line, double expected)
    {
        bool parsed = PercentageProgressParser.TryParse(line, out double percent);

        Assert.True(parsed);
        Assert.Equal(expected, percent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ordinary diagnostic text")]
    public void TryParse_WithoutPercentage_ReturnsFalse(string? line)
    {
        bool parsed = PercentageProgressParser.TryParse(line, out double percent);

        Assert.False(parsed);
        Assert.Equal(0d, percent);
    }
}
