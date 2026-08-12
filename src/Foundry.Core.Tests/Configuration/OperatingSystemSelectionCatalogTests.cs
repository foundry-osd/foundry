// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class OperatingSystemSelectionCatalogTests
{
    [Fact]
    public void SupportedReleaseIds_HaveUniquePositiveBuildMappings()
    {
        Assert.Equal(
            OperatingSystemSelectionCatalog.SupportedReleaseIds.Count,
            OperatingSystemSelectionCatalog.SupportedReleases.Count);
        Assert.Equal(
            OperatingSystemSelectionCatalog.SupportedReleases.Count,
            OperatingSystemSelectionCatalog.SupportedReleases.Select(release => release.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(OperatingSystemSelectionCatalog.SupportedReleases, release => Assert.True(release.Build > 0));
    }

    [Theory]
    [InlineData("23H2", 22631)]
    [InlineData("24H2", 26100)]
    [InlineData("25H2", 26200)]
    public void FindRelease_KnownRelease_ReturnsBuild(string releaseId, int expectedBuild)
    {
        OperatingSystemReleaseDefinition release = Assert.IsType<OperatingSystemReleaseDefinition>(
            OperatingSystemSelectionCatalog.FindRelease(releaseId));

        Assert.Equal(expectedBuild, release.Build);
    }
}
