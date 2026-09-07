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
using Foundry.Core.Services.Autopilot;

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
            byte[] bytes = await ReadBoundedAsync(profile.ConfigurationFilePath,
                AutopilotOfflineProfileValidator.MaximumContentLength, cancellationToken).ConfigureAwait(false);
            AutopilotOfflineProfileValidator.Validate(bytes);
            return bytes;
        }

        byte[]? deploymentKey = null;
        byte[] plaintext;
        try
        {
            deploymentKey = deploymentSecretKeySession.GetKeyCopy();
            // Two MiB accommodates Base64 expansion of the one-MiB profile plus envelope metadata.
            byte[] envelopeJson = await ReadBoundedAsync(profile.ConfigurationFilePath,
                2 * AutopilotOfflineProfileValidator.MaximumContentLength, cancellationToken).ConfigureAwait(false);
            SecretEnvelope? envelope = JsonSerializer.Deserialize<SecretEnvelope>(
                envelopeJson,
                ConfigurationJsonDefaults.SerializerOptions);
            if (envelope is null)
            {
                throw new InvalidDataException();
            }

            plaintext = DeployMediaSecretEnvelopeProtector.DecryptBytes(
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
        try
        {
            AutopilotOfflineProfileValidator.Validate(plaintext);
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, int maximumBytes, CancellationToken token)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        if (input.Length > maximumBytes) throw new InvalidDataException("The Autopilot profile file exceeds its size limit.");
        byte[] bytes = new byte[(int)input.Length];
        await input.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (await input.ReadAsync(new byte[1], token).ConfigureAwait(false) != 0)
            throw new InvalidDataException("The Autopilot profile file changed while it was read.");
        return bytes;
    }
}
