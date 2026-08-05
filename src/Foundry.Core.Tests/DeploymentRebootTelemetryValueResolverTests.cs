// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Telemetry;

namespace Foundry.Core.Tests;

public sealed class DeploymentRebootTelemetryValueResolverTests
{
    [Theory]
    [InlineData(false, 10, "manual", null)]
    [InlineData(true, 0, "immediate", null)]
    [InlineData(true, 42, "countdown", 42)]
    [InlineData(true, -1, "countdown", 10)]
    [InlineData(true, 3601, "countdown", 10)]
    public void Resolve_MapsAuthoredRebootPolicyToStableTelemetryValues(
        bool automaticRebootEnabled,
        int delaySeconds,
        string expectedMode,
        int? expectedDelaySeconds)
    {
        DeploymentRebootTelemetryValue result = DeploymentRebootTelemetryValueResolver.Resolve(
            automaticRebootEnabled,
            delaySeconds);

        Assert.Equal(expectedMode, result.Mode);
        Assert.Equal(expectedDelaySeconds, result.DelaySeconds);
    }
}
