// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>
/// Defines Foundry.Deploy behavior after a successful deployment.
/// </summary>
public sealed record DeployCompletionSettings
{
    /// <summary>
    /// Gets a value indicating whether the device reboots automatically.
    /// </summary>
    public bool AutomaticRebootEnabled { get; init; } = true;

    /// <summary>
    /// Gets the configured automatic reboot delay in seconds.
    /// </summary>
    public int AutomaticRebootDelaySeconds { get; init; } = DeploymentRebootDelay.DefaultSeconds;
}
