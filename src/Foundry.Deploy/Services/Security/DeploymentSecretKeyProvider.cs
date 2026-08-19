// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Configuration;
using Foundry.Utilities.Security;

namespace Foundry.Deploy.Services.Security;

public sealed class DeploymentSecretKeyProvider(
    IDeployConfigurationService configurationService,
    IDeploymentSecretKeySession deploymentSecretKeySession) : IDeploymentSecretKeyProvider
{
    private const string DeploymentKeyRelativePath = @"Config\Secrets\deployment-secrets.key";
    private const string LegacyKeyRelativePath = @"Config\Secrets\media-secrets.key";

    public async Task<byte[]> ReadAsync(string workspaceRootPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRootPath);
        DeployConfigurationLoadResult configuration = configurationService.LoadOptional();
        if (configuration.Exists && configuration.Document is null)
        {
            throw new InvalidOperationException("Deploy configuration is invalid.");
        }

        DeployProtectionSettings protection = configuration.Document?.Protection ?? new DeployProtectionSettings();
        bool requiresUnlock = DeploymentProtectionDetector.RequiresUnlock(protection) ||
                              DeploymentProtectionDetector.HasProtectedArtifacts(configuration, workspaceRootPath);
        if (requiresUnlock)
        {
            if (!deploymentSecretKeySession.IsUnlocked)
            {
                throw new InvalidOperationException("Deployment secrets are not unlocked.");
            }

            return deploymentSecretKeySession.GetKeyCopy();
        }

        string deploymentKeyPath = Path.Combine(workspaceRootPath, DeploymentKeyRelativePath);
        string keyPath = File.Exists(deploymentKeyPath)
            ? deploymentKeyPath
            : Path.Combine(workspaceRootPath, LegacyKeyRelativePath);
        if (!File.Exists(keyPath))
        {
            throw new FileNotFoundException("Deploy secret key was not found in the boot media configuration.", keyPath);
        }

        byte[] key = await File.ReadAllBytesAsync(keyPath, cancellationToken).ConfigureAwait(false);
        if (key.Length != AesGcmEncryption.KeySizeBytes)
        {
            throw new InvalidOperationException("Deploy secret key has an invalid length.");
        }

        return key;
    }
}
