// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>
/// Describes network settings consumed by Foundry.Deploy.
/// </summary>
public sealed record DeployNetworkSettings
{
    /// <summary>
    /// Gets network profile roaming settings.
    /// </summary>
    public DeployNetworkProfileRoamingSettings ProfileRoaming { get; init; } = new();
}
