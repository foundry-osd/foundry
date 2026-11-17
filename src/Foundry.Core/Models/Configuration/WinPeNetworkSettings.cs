// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Network;

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes the network profile roaming shared by the WinPE modes: Connect captures the provisioned profiles under
/// <see cref="ArtifactRootPath"/>, and Deploy stages them into the applied Windows.
/// </summary>
public sealed record WinPeNetworkSettings
{
    /// <summary>Gets the per-transport roaming opt-ins.</summary>
    public ConnectNetworkProfileRoamingSettings ProfileRoaming { get; init; } = new();

    /// <summary>Gets the WinPE directory that holds the captured roaming artifacts.</summary>
    public string ArtifactRootPath { get; init; } = NetworkProfileRoamingArtifacts.DefaultArtifactRootPath;
}
