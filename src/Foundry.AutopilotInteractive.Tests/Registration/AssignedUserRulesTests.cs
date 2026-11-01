// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.AutopilotInteractive.Services.Registration;

namespace Foundry.AutopilotInteractive.Tests.Registration;

public sealed class AssignedUserRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("user@contoso.com")]
    [InlineData(" user@contoso.com ")]
    public void IsValid_AcceptsBlankAndSimpleUpn(string? value)
    {
        Assert.True(AssignedUserRules.IsValid(value));
    }

    [Theory]
    [InlineData("user")]
    [InlineData("a@b@c.com")]
    [InlineData("us er@contoso.com")]
    [InlineData("@contoso.com")]
    [InlineData("user@")]
    public void IsValid_RejectsMissingAt_TwoAts_Spaces_EmptyParts(string value)
    {
        Assert.False(AssignedUserRules.IsValid(value));
    }

    [Fact]
    public void ToRequestedUser_ReturnsNull_ForBlank()
    {
        Assert.Null(AssignedUserRules.ToRequestedUser(null));
        Assert.Null(AssignedUserRules.ToRequestedUser("  "));
        Assert.Equal("user@contoso.com", AssignedUserRules.ToRequestedUser(" user@contoso.com "));
    }
}
