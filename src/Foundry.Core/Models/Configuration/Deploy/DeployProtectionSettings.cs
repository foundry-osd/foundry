// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>
/// Describes how protected deployment media unlocks its Deploy secret key.
/// </summary>
public sealed record DeployProtectionSettings
{
    /// <summary>
    /// Gets a value indicating whether the deployment media requires a password.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the password key derivation algorithm identifier.
    /// </summary>
    public string? KeyDerivationAlgorithm { get; init; }

    /// <summary>
    /// Gets the password key derivation iteration count.
    /// </summary>
    public int Iterations { get; init; }

    /// <summary>
    /// Gets the unpadded Base64URL-encoded password salt.
    /// </summary>
    public string? Salt { get; init; }

    /// <summary>
    /// Gets the password-wrapped Deploy key.
    /// </summary>
    public SecretEnvelope? ProtectedDeploymentKey { get; init; }
}
