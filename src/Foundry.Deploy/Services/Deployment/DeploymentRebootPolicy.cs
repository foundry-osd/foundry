// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Deploy.Models.Configuration;

namespace Foundry.Deploy.Services.Deployment;

public enum DeploymentRebootAction
{
    WaitForManualReboot,
    StartCountdown,
    RebootImmediately
}

/// <summary>
/// Resolves configured deployment completion settings into safe runtime reboot behavior.
/// </summary>
public sealed record DeploymentRebootPolicy(bool AutomaticRebootEnabled, int DelaySeconds)
{
    public DeploymentRebootAction Action => !AutomaticRebootEnabled
        ? DeploymentRebootAction.WaitForManualReboot
        : DelaySeconds == 0
            ? DeploymentRebootAction.RebootImmediately
            : DeploymentRebootAction.StartCountdown;

    public static DeploymentRebootPolicy Create(DeployCompletionSettings? settings)
    {
        settings ??= new DeployCompletionSettings();
        int delaySeconds = DeploymentRebootDelay.NormalizeRuntime(settings.AutomaticRebootDelaySeconds);

        return new DeploymentRebootPolicy(settings.AutomaticRebootEnabled, delaySeconds);
    }
}
