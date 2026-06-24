// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>
/// Describes whether Foundry.Deploy should import eligible network profile material before OOBE.
/// </summary>
public sealed record DeployNetworkProfileRoamingSettings
{
    /// <summary>
    /// Gets whether eligible Foundry-managed network profile roaming is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets whether explicitly configured PFX/private-key material may be imported.
    /// </summary>
    public bool IncludePrivateKeyMaterial { get; init; }

    /// <summary>
    /// Gets the artifact root path consumed by Foundry.Deploy inside WinPE.
    /// </summary>
    public string ArtifactRootPath { get; init; } = Foundry.Core.Models.Network.NetworkProfileRoamingArtifacts.DefaultArtifactRootPath;
}
