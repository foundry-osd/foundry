// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>
/// Describes customization settings consumed by Foundry.Deploy.
/// </summary>
public sealed record DeployCustomizationSettings
{
    /// <summary>
    /// Gets computer-name customization rules.
    /// </summary>
    public DeployMachineNamingSettings MachineNaming { get; init; } = new();

    /// <summary>
    /// Gets Windows OOBE customization settings applied during deployment.
    /// </summary>
    public DeployOobeSettings Oobe { get; init; } = new();

    /// <summary>
    /// Gets provisioned AppX removal settings applied before OOBE.
    /// </summary>
    public DeployAppxRemovalSettings AppxRemoval { get; init; } = new();

    /// <summary>
    /// Gets Windows AI component removal settings applied before OOBE.
    /// </summary>
    public DeployAiComponentRemovalSettings AiComponentRemoval { get; init; } = new();
}
