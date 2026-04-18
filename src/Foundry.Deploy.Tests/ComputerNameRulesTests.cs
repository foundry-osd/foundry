using Foundry.Deploy.Validation;

namespace Foundry.Deploy.Tests;

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
        Assert.Equal(ComputerNameRules.ValidationMessage, ComputerNameRules.GetValidationMessage("Computer-Name-Too-Long"));
    }
}
