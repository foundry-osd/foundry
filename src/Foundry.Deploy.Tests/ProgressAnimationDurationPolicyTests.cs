// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Controls;

namespace Foundry.Deploy.Tests;

public sealed class ProgressAnimationDurationPolicyTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(101, 100)]
    [InlineData(42.5, 42.5)]
    public void ClampTarget_ConstrainsProgressToValidRange(double input, double expected)
    {
        Assert.Equal(expected, ProgressAnimationDurationPolicy.ClampTarget(input));
    }

    [Fact]
    public void GetDuration_UsesShorterDurationForSmallChangesAndCapsLargeChanges()
    {
        TimeSpan smallChange = ProgressAnimationDurationPolicy.GetDuration(10, 12);
        TimeSpan largeChange = ProgressAnimationDurationPolicy.GetDuration(10, 90);

        Assert.InRange(smallChange.TotalMilliseconds, 180, 250);
        Assert.InRange(largeChange.TotalMilliseconds, 400, 450);
        Assert.True(largeChange > smallChange);
    }
}
