// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration.Deploy;

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Owns generated Deploy key material for one media creation operation.
/// </summary>
public sealed class DeploymentMediaProtectionMaterial : IDisposable
{
    internal DeploymentMediaProtectionMaterial(byte[] deploymentKey, DeployProtectionSettings settings)
    {
        DeploymentKey = deploymentKey;
        Settings = settings;
    }

    public byte[] DeploymentKey { get; }

    public DeployProtectionSettings Settings { get; }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(DeploymentKey);
    }
}
