// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class ComputerNameTemplateTests
{
    [Theory]
    [InlineData("PC-${SERIALNUMBER}", true)]
    [InlineData("${SERIALNUMBER}", true)]
    [InlineData("PC-001", false)]
    [InlineData("", false)]
    public void ContainsVariable_DetectsBraceToken(string value, bool expected)
    {
        Assert.Equal(expected, ComputerNameTemplate.ContainsVariable(value));
    }

    [Fact]
    public void Expand_ReplacesSerialNumberCaseInsensitively()
    {
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SERIALNUMBER"] = "5CG1234ABC"
        };

        Assert.Equal("PC-5CG1234ABC", ComputerNameTemplate.Expand("PC-${serialnumber}", variables));
    }

    [Fact]
    public void Expand_SupportsVariableOnlyName()
    {
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SERIALNUMBER"] = "5CG1234ABC"
        };

        Assert.Equal("5CG1234ABC", ComputerNameTemplate.Expand("${SERIALNUMBER}", variables));
    }

    [Fact]
    public void Expand_DropsUnknownVariables()
    {
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SERIALNUMBER"] = "ABC"
        };

        Assert.Equal("PC-ABC", ComputerNameTemplate.Expand("PC-${UNKNOWN}${SERIALNUMBER}", variables));
    }

    [Fact]
    public void ExpandAndNormalize_TruncatesToFifteenCharacters()
    {
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SERIALNUMBER"] = "0123456789ABCDEF"
        };

        string result = ComputerNameTemplate.ExpandAndNormalize("${SERIALNUMBER}", variables);

        Assert.Equal("0123456789ABCDE", result);
        Assert.Equal(ComputerNameRules.MaxLength, result.Length);
    }

    [Fact]
    public void ExpandAndNormalize_DropsInvalidCharactersFromExpandedValue()
    {
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            // Some vendors emit serials with spaces or slashes; those are dropped by the name rules.
            ["SERIALNUMBER"] = "5CG 12/34"
        };

        Assert.Equal("PC-5CG1234", ComputerNameTemplate.ExpandAndNormalize("PC-${SERIALNUMBER}", variables));
    }

    [Fact]
    public void NormalizePrefix_KeepsVariableTokenIntact()
    {
        Assert.Equal("PC-${SERIALNUMBER}", ComputerNameTemplate.NormalizePrefix("PC-${SERIALNUMBER}"));
        Assert.Equal("${SERIALNUMBER}", ComputerNameTemplate.NormalizePrefix("${SERIALNUMBER}"));
    }

    [Fact]
    public void NormalizePrefix_ForPlainValue_TruncatesToFifteen()
    {
        Assert.Equal("ABCDEFGHIJKLMNO", ComputerNameTemplate.NormalizePrefix("ABCDEFGHIJKLMNOPQRS"));
    }

    [Theory]
    [InlineData("$")]
    [InlineData("${")]
    [InlineData("${SERIAL")]
    [InlineData("PC-${SERIALNUMBER}")]
    public void NormalizePrefix_PreservesTemplateCharactersWhileTyping(string value)
    {
        // A lone $ (or partial token) must survive so the user can finish typing ${VARIABLE}.
        Assert.Equal(value, ComputerNameTemplate.NormalizePrefix(value));
    }

    [Theory]
    [InlineData("PC-${SERIALNUMBER}", true)]
    [InlineData("${SERIALNUMBER}", true)]
    [InlineData("${MODEL}-01", true)]
    [InlineData("with space", false)]
    [InlineData("PC-001", true)]
    public void IsValidPrefix_ValidatesTemplatesAndPlainNames(string value, bool expected)
    {
        Assert.Equal(expected, ComputerNameTemplate.IsValidPrefix(value));
    }
}
