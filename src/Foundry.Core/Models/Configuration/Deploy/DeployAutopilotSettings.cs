// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>
/// Describes the reduced Autopilot runtime settings consumed by Foundry.Deploy.
/// </summary>
public sealed record DeployAutopilotSettings
{
    /// <summary>
    /// Gets whether Autopilot provisioning is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the provisioning method selected by Foundry OSD.
    /// </summary>
    public AutopilotProvisioningMode ProvisioningMode { get; init; } = AutopilotProvisioningMode.JsonProfile;

    /// <summary>
    /// Gets the profile folder name selected for staging.
    /// </summary>
    public string? DefaultProfileFolderName { get; init; }

    /// <summary>
    /// Gets runtime metadata required for hardware hash upload mode.
    /// </summary>
    public DeployAutopilotHardwareHashUploadSettings HardwareHashUpload { get; init; } = new();
}
