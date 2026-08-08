// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Globalization;

namespace Foundry.Utilities.Tests.Globalization;

public sealed class CultureCodeTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("  fr_fr  ", "fr-FR")]
    [InlineData("EN-us", "en-US")]
    [InlineData("custom_value", "custom-value")]
    public void Canonicalize_NormalizesWithoutRejectingUnknownValues(string? value, string expected)
    {
        Assert.Equal(expected, CultureCode.Canonicalize(value));
    }

    [Fact]
    public void NormalizeForComparison_ReturnsInvariantLowercase()
    {
        Assert.Equal("pt-br", CultureCode.NormalizeForComparison("PT_br"));
    }
}
