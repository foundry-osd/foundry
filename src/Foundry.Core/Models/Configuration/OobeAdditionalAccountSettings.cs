// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes one additional local account provisioned during Windows OOBE.
/// </summary>
public sealed record OobeAdditionalAccountSettings
{
    /// <summary>
    /// Gets the stable identifier used to associate session-only secrets with this account.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Windows username to create.
    /// </summary>
    public string? UserName { get; init; }

    /// <summary>
    /// Gets the local account type to provision.
    /// </summary>
    public OobeAccountType Type { get; init; }
}
