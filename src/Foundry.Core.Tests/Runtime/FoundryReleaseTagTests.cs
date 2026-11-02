// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;

namespace Foundry.Core.Tests.Runtime;

public sealed class FoundryReleaseTagTests
{
    [Fact]
    public void Resolve_OverrideWins_EvenForPublishedVersion()
    {
        FoundryReleaseSelection selection = FoundryReleaseTag.Resolve("26.11.2.1",
            name => name == FoundryReleaseTag.ReleaseTagVariable ? " v26.1.1.1 " : null, FoundryReleaseTag.ReleaseTagVariable);

        Assert.Equal("v26.1.1.1", selection.Tag);
    }

    [Fact]
    public void Resolve_FirstOverrideHasPriority()
    {
        var values = new Dictionary<string, string> { ["FOUNDRY_DEPLOY_RELEASE_TAG"] = "v26.9.14.1", [FoundryReleaseTag.ReleaseTagVariable] = "v26.1.1.1" };

        FoundryReleaseSelection selection = FoundryReleaseTag.Resolve("26.11.2.1", name => values.GetValueOrDefault(name),
            "FOUNDRY_DEPLOY_RELEASE_TAG", FoundryReleaseTag.ReleaseTagVariable);

        Assert.Equal("v26.9.14.1", selection.Tag);
    }

    [Fact]
    public void Resolve_PublishedVersion_ReturnsItsTag()
    {
        FoundryReleaseSelection selection = FoundryReleaseTag.Resolve("26.11.2.1+abcdef", _ => null, FoundryReleaseTag.ReleaseTagVariable);

        Assert.Equal("v26.11.2.1", selection.Tag);
        Assert.False(selection.IsLatest);
        Assert.Equal("https://api.github.com/repos/foundry-osd/foundry/releases/tags/v26.11.2.1", selection.ApiUrl);
    }

    [Fact]
    public void Resolve_UnpublishedVersion_ReturnsLatest()
    {
        FoundryReleaseSelection selection = FoundryReleaseTag.Resolve(FoundryReleaseTag.UnpublishedVersion + "+local", _ => null);

        Assert.True(selection.IsLatest);
        Assert.Equal("https://api.github.com/repos/foundry-osd/foundry/releases/latest", selection.ApiUrl);
    }

    [Theory]
    [InlineData("26.11.3")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-version")]
    public void Resolve_VersionWithoutFourParts_Throws(string? version)
    {
        Assert.Throws<InvalidDataException>(() => FoundryReleaseTag.Resolve(version, _ => null, FoundryReleaseTag.ReleaseTagVariable));
    }

    [Fact]
    public void ApiUrl_EscapesTheTag()
    {
        Assert.EndsWith("/tags/v1%2F2", new FoundryReleaseSelection("v1/2").ApiUrl);
    }
}
