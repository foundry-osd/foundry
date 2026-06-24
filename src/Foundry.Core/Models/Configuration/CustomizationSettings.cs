// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Stores user-authored deployment customization settings.
/// </summary>
public sealed record CustomizationSettings
{
    /// <summary>
    /// Gets hostname generation settings for deployed machines.
    /// </summary>
    public MachineNamingSettings MachineNaming { get; init; } = new();

    /// <summary>
    /// Gets Windows OOBE customization settings.
    /// </summary>
    public OobeSettings Oobe { get; init; } = new();

    /// <summary>
    /// Gets provisioned AppX removal settings.
    /// </summary>
    public AppxRemovalSettings AppxRemoval { get; init; } = new();

    /// <summary>
    /// Gets Windows AI component removal settings.
    /// </summary>
    public AiComponentRemovalSettings AiComponentRemoval { get; init; } = new();
}
