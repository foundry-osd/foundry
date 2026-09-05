// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>
/// Describes one local account to provision during OOBE at deployment time.
/// </summary>
public sealed record DeployOobeAdditionalAccountSettings
{
    /// <summary>
    /// Gets the stable identifier used to associate transient secrets with this account.
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

    /// <summary>
    /// Gets a value indicating whether the account should be provisioned with a blank password.
    /// </summary>
    public bool PasswordIsBlank { get; init; }

    /// <summary>
    /// Gets the encrypted password envelope for deployment-time use.
    /// </summary>
    public SecretEnvelope? PasswordSecret { get; init; }
}
