// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models.Configuration;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Resolves configured deployment completion settings into safe runtime reboot behavior.
/// </summary>
public sealed record DeploymentRebootPolicy(bool AutomaticRebootEnabled, int DelaySeconds)
{
    public const int DefaultDelaySeconds = 10;

    public const int MaximumDelaySeconds = 3600;

    public bool ShouldRebootImmediately => AutomaticRebootEnabled && DelaySeconds == 0;

    public bool ShouldStartCountdown => AutomaticRebootEnabled && DelaySeconds > 0;

    public static DeploymentRebootPolicy Create(DeployCompletionSettings? settings)
    {
        settings ??= new DeployCompletionSettings();
        int delaySeconds = settings.AutomaticRebootDelaySeconds is >= 0 and <= MaximumDelaySeconds
            ? settings.AutomaticRebootDelaySeconds
            : DefaultDelaySeconds;

        return new DeploymentRebootPolicy(settings.AutomaticRebootEnabled, delaySeconds);
    }
}
