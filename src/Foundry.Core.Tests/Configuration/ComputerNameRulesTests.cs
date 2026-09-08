// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class ComputerNameRulesTests
{
    [Fact]
    public void Normalize_RemovesUnsupportedCharactersAndTruncatesToMaximumLength()
    {
        string normalized = ComputerNameRules.Normalize(" PC_01-Alpha!BetaGamma ");

        Assert.Equal("PC01-AlphaBetaG", normalized);
    }

    [Fact]
    public void IsValid_ReturnsFalseWhenValueExceedsMaximumLength()
    {
        bool isValid = ComputerNameRules.IsValid("Computer-Name-Too-Long");

        Assert.False(isValid);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("123456789012345", false)]
    [InlineData("PC1234567890123", true)]
    [InlineData("1234567890123PC", true)]
    [InlineData("123-456", true)]
    public void IsValid_RejectsNumericOnlyNames(string computerName, bool expected)
    {
        Assert.Equal(expected, ComputerNameRules.IsValid(computerName));
    }
}
