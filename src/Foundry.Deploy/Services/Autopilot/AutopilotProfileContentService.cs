// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Security;

namespace Foundry.Deploy.Services.Autopilot;

public sealed class AutopilotProfileContentService(IDeploymentSecretKeySession deploymentSecretKeySession)
    : IAutopilotProfileContentService
{
    public async Task<byte[]> ReadAsync(
        AutopilotProfileCatalogItem profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.IsProtected)
        {
            return await File.ReadAllBytesAsync(profile.ConfigurationFilePath, cancellationToken).ConfigureAwait(false);
        }

        byte[]? deploymentKey = null;
        try
        {
            deploymentKey = deploymentSecretKeySession.GetKeyCopy();
            byte[] envelopeJson = await File.ReadAllBytesAsync(profile.ConfigurationFilePath, cancellationToken).ConfigureAwait(false);
            SecretEnvelope? envelope = JsonSerializer.Deserialize<SecretEnvelope>(
                envelopeJson,
                ConfigurationJsonDefaults.SerializerOptions);
            if (envelope is null)
            {
                throw new InvalidDataException();
            }

            return DeployMediaSecretEnvelopeProtector.DecryptBytes(
                envelope,
                deploymentKey,
                DeployMediaSecretEnvelopeProtector.DeploymentKeyId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
                                   InvalidOperationException or ArgumentException or FormatException or
                                   CryptographicException or InvalidDataException)
        {
            throw new InvalidDataException("Protected Autopilot profile could not be read.", ex);
        }
        finally
        {
            if (deploymentKey is not null)
            {
                CryptographicOperations.ZeroMemory(deploymentKey);
            }
        }
    }
}
