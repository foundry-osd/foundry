// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Profiles;

namespace Foundry.Core.Tests.Profiles;

public sealed class SharedProfileLocationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData(@"..\outside")]
    [InlineData("name.")]
    [InlineData("name ")]
    [InlineData("name\t")]
    [InlineData("name\0suffix")]
    [InlineData("name\u0085suffix")]
    [InlineData("name:stream")]
    [InlineData("name*pattern")]
    [InlineData("name?pattern")]
    [InlineData("name|suffix")]
    [InlineData("name<suffix")]
    [InlineData("name>suffix")]
    [InlineData("name\"suffix")]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("CON .txt")]
    [InlineData("PRN")]
    [InlineData("Aux.xml")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("com9.backup")]
    [InlineData("LPT1")]
    [InlineData("lpt9.backup")]
    [InlineData("COM¹.txt")]
    [InlineData("LPT²")]
    [InlineData("COM³")]
    public void IsValidName_RejectsUnsafeOrAmbiguousFolderNames(string? name)
    {
        Assert.False(SharedProfileLocation.IsValidName(name));
        Assert.Throws<ArgumentException>(() => SharedProfileLocation.Resolve(@"\\server\share", name!));
    }

    [Theory]
    [InlineData("Team settings")]
    [InlineData("Équipe déploiement")]
    [InlineData("部署設定")]
    [InlineData("إعدادات الفريق")]
    [InlineData("COM10")]
    [InlineData("LPT0")]
    [InlineData("Console")]
    [InlineData("Team.v2")]
    public void Resolve_PreservesHumanNamesWithoutSanitizing(string name)
    {
        Assert.True(SharedProfileLocation.IsValidName(name));
        Assert.Equal(@"\\server\share\Foundry\" + name, SharedProfileLocation.Resolve(@"\\server\share", name));
    }

    [Fact]
    public void IsValidName_Enforces120CharacterBoundary()
    {
        Assert.True(SharedProfileLocation.IsValidName(new string('a', 120)));
        Assert.False(SharedProfileLocation.IsValidName(new string('a', 121)));
    }

    [Theory]
    [InlineData(@"\\server\share")]
    [InlineData(@"\\server\share\")]
    [InlineData(@"\\server\share\\")]
    public void Resolve_TrailingParentSeparatorsKeepOneDedicatedPath(string parent)
    {
        Assert.Equal(@"\\server\share\Foundry\Team", SharedProfileLocation.Resolve(parent, "Team"));
    }

    [Fact]
    public void Resolve_NestedParentIsPreservedAndForwardSeparatorsAreNormalized()
    {
        Assert.Equal(@"\\server\share\existing folder\Foundry\Team", SharedProfileLocation.Resolve(@"\\server\share/existing folder/", "Team"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\share")]
    [InlineData(@"\server\share")]
    [InlineData(@"\\server")]
    [InlineData(@"\\server\")]
    [InlineData(@"\\\server\share")]
    [InlineData(@"\\?\UNC\server\share")]
    [InlineData(@"\\.\server\share")]
    [InlineData(@"\\server\share\..\outside")]
    [InlineData(@"\\server\share/./child")]
    [InlineData(@"\\server\share\child.")]
    [InlineData(@"\\server\share\child ")]
    [InlineData(@"\\server\share:stream")]
    [InlineData(@"\\server\\share")]
    public void Resolve_RejectsInvalidUncParents(string? parent)
    {
        Assert.ThrowsAny<ArgumentException>(() => SharedProfileLocation.Resolve(parent!, "Team"));
    }
}
