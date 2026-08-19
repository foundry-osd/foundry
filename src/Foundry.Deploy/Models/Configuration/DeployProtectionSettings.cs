// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Models.Configuration;

/// <summary>
/// Describes optional password protection for deployment media.
/// </summary>
public sealed record DeployProtectionSettings
{
    public bool IsEnabled { get; init; }

    public string KeyDerivationAlgorithm { get; init; } = string.Empty;

    public int Iterations { get; init; }

    public string Salt { get; init; } = string.Empty;

    public SecretEnvelope ProtectedDeploymentKey { get; init; } = new();
}
