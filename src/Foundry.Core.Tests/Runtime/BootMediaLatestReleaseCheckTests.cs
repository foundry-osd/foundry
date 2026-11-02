// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Runtime;

namespace Foundry.Core.Tests.Runtime;

public sealed class BootMediaLatestReleaseCheckTests
{
    [Fact]
    public void Create_ReturnsACheckForAnEligiblePublishedRuntimeWithoutAdvice()
    {
        BootMediaLatestReleaseCheck? check = BootMediaLatestReleaseCheck.Create("26.10.3.1", "26.10.3.1", isEligibleRuntime: true);

        Assert.Equal(new BootMediaLatestReleaseCheck("26.10.3.1", "26.10.3.1"), check);
    }

    [Theory]
    [InlineData("26.10.3.1", "26.10.3.1", false)]
    [InlineData("26.10.3.1", "1.0.0.0", true)]
    [InlineData("26.10.3.1", "26.10.3.2", true)]
    [InlineData(null, "26.10.3.1", true)]
    public void Create_ReturnsNullWhenTheLatestReleaseCannotChangeTheAdvice(string? authoringVersion, string runtimeVersion, bool isEligibleRuntime)
    {
        Assert.Null(BootMediaLatestReleaseCheck.Create(authoringVersion, runtimeVersion, isEligibleRuntime));
    }

    [Theory]
    [InlineData("26.11.2.1", BootMediaUpdateReason.NewerRelease)]
    [InlineData("26.10.3.1", BootMediaUpdateReason.None)]
    [InlineData(null, BootMediaUpdateReason.None)]
    public async Task EvaluateAsync_ComparesTheBootMediaWithTheLatestRelease(string? latestReleaseVersion, BootMediaUpdateReason expected)
    {
        var check = new BootMediaLatestReleaseCheck("26.10.3.1", "26.10.3.1");

        BootMediaUpdateReason reason = await check.EvaluateAsync(_ => Task.FromResult(latestReleaseVersion), CancellationToken.None);

        Assert.Equal(expected, reason);
    }
}
