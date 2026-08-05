// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentRebootPolicyTests
{
    [Theory]
    [InlineData(true, 10, true, false, 10)]
    [InlineData(true, 0, false, true, 0)]
    [InlineData(false, 10, false, false, 10)]
    [InlineData(true, -1, true, false, 10)]
    [InlineData(true, 3601, true, false, 10)]
    public void Create_ResolvesExpectedBehavior(
        bool automaticRebootEnabled,
        int configuredDelaySeconds,
        bool shouldStartCountdown,
        bool shouldRebootImmediately,
        int expectedDelaySeconds)
    {
        DeploymentRebootPolicy policy = DeploymentRebootPolicy.Create(
            new DeployCompletionSettings
            {
                AutomaticRebootEnabled = automaticRebootEnabled,
                AutomaticRebootDelaySeconds = configuredDelaySeconds
            });

        Assert.Equal(automaticRebootEnabled, policy.AutomaticRebootEnabled);
        Assert.Equal(expectedDelaySeconds, policy.DelaySeconds);
        Assert.Equal(shouldStartCountdown, policy.ShouldStartCountdown);
        Assert.Equal(shouldRebootImmediately, policy.ShouldRebootImmediately);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3600)]
    public void Create_PreservesValidDelayBoundaries(int configuredDelaySeconds)
    {
        DeploymentRebootPolicy policy = DeploymentRebootPolicy.Create(
            new DeployCompletionSettings { AutomaticRebootDelaySeconds = configuredDelaySeconds });

        Assert.Equal(configuredDelaySeconds, policy.DelaySeconds);
    }
}
