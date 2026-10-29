// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Services.Security;
using Foundry.Deploy.Models.Configuration;

namespace Foundry.Deploy.Services.Security;

/// <summary>
/// Unlocks protected deployment media by unwrapping the deployment key with the shared Core implementation
/// and storing it in the Deploy secret key session.
/// </summary>
public sealed class DeploymentProtectionUnlockService(IDeploymentSecretKeySession session)
    : IDeploymentProtectionUnlockService
{
    public bool TryUnlock(DeployProtectionSettings settings, ReadOnlySpan<char> password)
    {
        ArgumentNullException.ThrowIfNull(settings);
        session.Clear();

        if (!DeploymentProtectionDetector.RequiresUnlock(settings))
        {
            return true;
        }

        byte[]? deploymentKey = DeploymentKeyProtection.TryUnwrap(
            settings.KeyDerivationAlgorithm,
            settings.Iterations,
            settings.Salt,
            settings.ProtectedDeploymentKey,
            password);
        if (deploymentKey is null)
        {
            return false;
        }

        try
        {
            session.SetKey(deploymentKey);
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(deploymentKey);
        }
    }
}
