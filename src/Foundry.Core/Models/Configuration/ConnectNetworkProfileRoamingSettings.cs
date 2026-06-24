// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes whether Foundry.Connect should capture eligible network profile material for Windows import.
/// </summary>
public sealed record ConnectNetworkProfileRoamingSettings
{
    /// <summary>
    /// Gets whether eligible Foundry-managed network profile roaming is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets whether explicitly configured PFX/private-key material may be included.
    /// </summary>
    public bool IncludePrivateKeyMaterial { get; init; }
}
