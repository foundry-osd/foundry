// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class DeploymentRebootDelayTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.1, 1)]
    [InlineData(10.1, 11)]
    [InlineData(-1, 0)]
    [InlineData(3601, 3600)]
    public void NormalizeAuthoring_ProducesSafeWholeSeconds(double value, int expected)
    {
        Assert.Equal(expected, DeploymentRebootDelay.NormalizeAuthoring(value));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NormalizeAuthoring_WhenValueIsNotFinite_UsesDefault(double value)
    {
        Assert.Equal(DeploymentRebootDelay.DefaultSeconds, DeploymentRebootDelay.NormalizeAuthoring(value));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(42, 42)]
    [InlineData(3600, 3600)]
    [InlineData(-1, 10)]
    [InlineData(3601, 10)]
    public void NormalizeRuntime_UsesDefaultOnlyForInvalidValues(int value, int expected)
    {
        Assert.Equal(expected, DeploymentRebootDelay.NormalizeRuntime(value));
    }
}
