// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Sharing;

namespace Foundry.Core.Tests.Sharing;

public sealed class PassphrasePolicyTests
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";

    [Fact]
    public void Validate_ElevenChars_ReturnsTooShort()
    {
        Assert.Equal(PassphraseValidation.TooShort, PassphrasePolicy.Validate("abcdefghijk"));
    }

    [Fact]
    public void Validate_TwelveSameChars_ReturnsRepetitive()
    {
        Assert.Equal(PassphraseValidation.Repetitive, PassphrasePolicy.Validate("aaaaaaaaaaaa"));
    }

    [Fact]
    public void Validate_TwelveMixedChars_ReturnsValid()
    {
        Assert.Equal(PassphraseValidation.Valid, PassphrasePolicy.Validate("aaaaaaaaaaab"));
    }

    [Fact]
    public void Generate_ReturnsFiveGroupsOfFourFromAlphabetAndValidates()
    {
        string passphrase = PassphrasePolicy.Generate();

        string[] groups = passphrase.Split('-');
        Assert.Equal(5, groups.Length);
        Assert.All(groups, group => Assert.Equal(4, group.Length));
        Assert.All(string.Concat(groups), character => Assert.Contains(character, Alphabet));
        Assert.Equal(PassphraseValidation.Valid, PassphrasePolicy.Validate(passphrase));
    }
}
