// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes network runtime settings consumed by Foundry.Connect.
/// </summary>
public sealed record ConnectNetworkSettings
{
    /// <summary>
    /// Gets network profile roaming settings.
    /// </summary>
    public ConnectNetworkProfileRoamingSettings ProfileRoaming { get; init; } = new();
}
