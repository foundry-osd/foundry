// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class DeploymentProtectionPasswordRulesTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("1234567", false)]
    [InlineData("12345678", true)]
    public void IsValid_RequiresEightCharacters(string? password, bool expected)
    {
        Assert.Equal(expected, DeploymentProtectionPasswordRules.IsValid(password.AsSpan()));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("1234567", false)]
    [InlineData("12345678", true)]
    [InlineData("12345678901", true)]
    [InlineData("123456789012", false)]
    public void ShouldRecommendStrongerPassword_OnlyRecommendsForValidValuesBelowTwelveCharacters(
        string? password,
        bool expected)
    {
        Assert.Equal(expected, DeploymentProtectionPasswordRules.ShouldRecommendStrongerPassword(password.AsSpan()));
    }
}
