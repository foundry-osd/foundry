// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentRebootPolicyTests
{
    [Theory]
    [InlineData(true, 10, DeploymentRebootAction.StartCountdown, 10)]
    [InlineData(true, 0, DeploymentRebootAction.RebootImmediately, 0)]
    [InlineData(false, 10, DeploymentRebootAction.WaitForManualReboot, 10)]
    [InlineData(true, -1, DeploymentRebootAction.StartCountdown, 10)]
    [InlineData(true, 3601, DeploymentRebootAction.StartCountdown, 10)]
    public void Create_ResolvesExpectedBehavior(
        bool automaticRebootEnabled,
        int configuredDelaySeconds,
        DeploymentRebootAction expectedAction,
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
        Assert.Equal(expectedAction, policy.Action);
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
