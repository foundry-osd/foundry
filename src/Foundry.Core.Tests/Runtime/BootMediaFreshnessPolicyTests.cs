// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Runtime;

namespace Foundry.Core.Tests.Runtime;

public sealed class BootMediaFreshnessPolicyTests
{
    [Theory]
    [InlineData(null, "26.10.3.2", true, BootMediaUpdateReason.UnknownAuthoringVersion)]
    [InlineData("", "26.10.3.2", true, BootMediaUpdateReason.UnknownAuthoringVersion)]
    [InlineData("   ", "26.10.3.2", true, BootMediaUpdateReason.UnknownAuthoringVersion)]
    [InlineData("unknown", "26.10.3.2", true, BootMediaUpdateReason.UnknownAuthoringVersion)]
    [InlineData("26.10.3.1", "26.10.3.2", true, BootMediaUpdateReason.NewerRelease)]
    [InlineData("26.10.2.9", "26.10.3.2", true, BootMediaUpdateReason.NewerRelease)]
    [InlineData("26.9.30.2", "26.10.3.2", true, BootMediaUpdateReason.NewerRelease)]
    [InlineData("25.12.31.9", "26.1.1.1", true, BootMediaUpdateReason.NewerRelease)]
    [InlineData("26.10.3.2", "26.10.3.2", true, BootMediaUpdateReason.None)]
    [InlineData("26.10.3.3", "26.10.3.2", true, BootMediaUpdateReason.None)]
    [InlineData(null, "26.10.3.2", false, BootMediaUpdateReason.None)]
    [InlineData("26.10.3.1", "26.10.3.2", false, BootMediaUpdateReason.None)]
    [InlineData(null, "1.0.0.0", true, BootMediaUpdateReason.None)]
    [InlineData("26.10.3.1", "invalid", true, BootMediaUpdateReason.None)]
    public void Evaluate_UsesAuthoringReleaseAndLegacyFallback(
        string? authoringVersion,
        string? runtimeVersion,
        bool isEligibleRuntime,
        BootMediaUpdateReason expectedReason)
    {
        Assert.Equal(expectedReason, BootMediaFreshnessPolicy.Evaluate(authoringVersion, runtimeVersion, isEligibleRuntime));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("26.2.29.1")]
    [InlineData("26.9.31.1")]
    [InlineData("1.0.0.0")]
    [InlineData("26.10.3.0")]
    [InlineData("26.10.3.65535")]
    [InlineData("v26.10.3.1")]
    [InlineData("26.10.3-build.1")]
    [InlineData("26.10.3.1-preview")]
    [InlineData("26.10.3.1+hash")]
    [InlineData("26.10.3")]
    [InlineData("26.10.3.1.2")]
    public void TryParseReleaseVersion_RequiresPublishedNumericContract(string? value)
    {
        Assert.False(BootMediaFreshnessPolicy.TryParseReleaseVersion(value, out Version? version));
        Assert.Null(version);
    }

    [Theory]
    [InlineData("24.2.29.1", 24, 2, 29, 1)]
    [InlineData(" 26.10.3.2 ", 26, 10, 3, 2)]
    [InlineData("00.1.1.65534", 0, 1, 1, 65534)]
    public void TryParseReleaseVersion_AcceptsValidCalendarReleases(
        string value,
        int year,
        int month,
        int day,
        int build)
    {
        Assert.True(BootMediaFreshnessPolicy.TryParseReleaseVersion(value, out Version? version));
        Assert.Equal(new Version(year, month, day, build), version);
    }
}
