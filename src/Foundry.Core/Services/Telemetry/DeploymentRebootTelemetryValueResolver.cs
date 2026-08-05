// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Telemetry;

/// <summary>
/// Resolves authored deployment reboot settings into stable telemetry values.
/// </summary>
public static class DeploymentRebootTelemetryValueResolver
{
    /// <summary>
    /// Maps an authored reboot policy to a stable mode and optional countdown delay.
    /// </summary>
    public static DeploymentRebootTelemetryValue Resolve(bool automaticRebootEnabled, int delaySeconds)
    {
        int normalizedDelaySeconds = DeploymentRebootDelay.NormalizeRuntime(delaySeconds);

        if (!automaticRebootEnabled)
        {
            return new DeploymentRebootTelemetryValue("manual", null);
        }

        return normalizedDelaySeconds == 0
            ? new DeploymentRebootTelemetryValue("immediate", null)
            : new DeploymentRebootTelemetryValue("countdown", normalizedDelaySeconds);
    }
}

/// <summary>
/// Represents the telemetry-safe form of an authored deployment reboot policy.
/// </summary>
public sealed record DeploymentRebootTelemetryValue(string Mode, int? DelaySeconds);
