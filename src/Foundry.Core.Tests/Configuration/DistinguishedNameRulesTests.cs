// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class DistinguishedNameRulesTests
{
    [Theory]
    [InlineData("OU=Workstations,DC=corp,DC=test", true)]
    [InlineData("ou=Laptops,OU=Workstations,DC=corp,DC=test", true)]
    [InlineData("CN=Computers,DC=corp,DC=test", false)]
    [InlineData("DC=corp,DC=test", false)]
    [InlineData("OU=A+CN=B,DC=corp,DC=test", false)]
    [InlineData("not-a-dn", false)]
    public void OnlyAnOrganizationalUnitIsAJoinTarget(string distinguishedName, bool expected)
    {
        Assert.Equal(expected, DistinguishedNameRules.IsOrganizationalUnit(distinguishedName));
    }

    [Theory]
    [InlineData("OU=Sales\\, West,DC=contoso,DC=test", "Sales, West")]
    [InlineData("OU=Sales\\2C West,DC=contoso,DC=test", "Sales, West")]
    [InlineData("OU=\\C3\\89quipe,DC=contoso,DC=test", "Équipe")]
    [InlineData("OU=研发,DC=contoso,DC=test", "研发")]
    public void ParsesEscapedAndUnicodeValues(string dn, string value)
    {
        Assert.True(DistinguishedNameRules.TryParse(dn, out ParsedDistinguishedName parsed));
        Assert.Equal(value, parsed.Rdns[0].Attributes[0].Value);
        Assert.Equal("DC=contoso,DC=test", parsed.Parent);
        Assert.True(DistinguishedNameRules.IsWithinDomain(dn, "CONTOSO.TEST."));
        Assert.False(DistinguishedNameRules.IsWithinDomain(dn, "other.test"));
    }

    [Theory]
    [InlineData("OU=Bad\\")]
    [InlineData("OU=Bad\\q,DC=contoso,DC=test")]
    [InlineData("OU=Bad\\C3,DC=contoso,DC=test")]
    [InlineData("OU=Bad,DC=contoso,,DC=test")]
    [InlineData("OU=Bad\0Name,DC=contoso,DC=test")]
    [InlineData("OU= unescaped,DC=contoso,DC=test")]
    [InlineData("OU=unescaped ,DC=contoso,DC=test")]
    [InlineData("OU=Bad;Name,DC=contoso,DC=test")]
    public void RejectsMalformedNames(string dn) => Assert.False(DistinguishedNameRules.TryParse(dn, out _));

    [Fact]
    public void DomainSuffixCannotBeAnEscapedOrMultivaluedLookalike()
    {
        Assert.False(DistinguishedNameRules.IsWithinDomain("OU=PC,DC=contoso+OU=evil,DC=test", "contoso.test"));
        Assert.False(DistinguishedNameRules.IsWithinDomain("OU=PC,DC=contoso\\,DC=test", "contoso.test"));
        Assert.False(DistinguishedNameRules.IsWithinDomain("OU=PC,DC=child,DC=contoso,DC=test", "contoso.test"));
    }

    [Theory]
    [InlineData("OU=Sales=West,DC=contoso,DC=test")]
    [InlineData("OU=Sales\\=West,DC=contoso,DC=test")]
    public void ParsesEqualsWithinAnAttributeValue(string dn)
    {
        Assert.True(DistinguishedNameRules.TryParse(dn, out ParsedDistinguishedName parsed));
        Assert.Equal("Sales=West", parsed.Rdns[0].Attributes[0].Value);
    }

    [Fact]
    public void RejectsUnpairedUnicodeSurrogates()
    {
        Assert.False(DistinguishedNameRules.TryParse("OU=Bad\uD800,DC=contoso,DC=test", out _));
    }
}
