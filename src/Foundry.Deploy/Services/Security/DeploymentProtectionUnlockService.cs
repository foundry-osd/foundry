// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Deploy.Models.Configuration;
using Foundry.Utilities.Security;

namespace Foundry.Deploy.Services.Security;

public sealed class DeploymentProtectionUnlockService(IDeploymentSecretKeySession session)
    : IDeploymentProtectionUnlockService
{
    public const string KeyDerivationAlgorithm = "pbkdf2-sha256";
    public const string EnvelopeKind = "encrypted";
    public const string EnvelopeAlgorithm = "aes-gcm-v1";
    public const string PasswordDerivedKeyId = "deployment-password";

    public bool TryUnlock(DeployProtectionSettings settings, ReadOnlySpan<char> password)
    {
        ArgumentNullException.ThrowIfNull(settings);
        session.Clear();

        if (!settings.IsEnabled)
        {
            return true;
        }

        byte[]? derivedKey = null;
        byte[]? deploymentKey = null;
        try
        {
            ValidateSettings(settings);
            byte[] salt = Base64Url.Decode(settings.Salt);
            try
            {
                if (salt.Length != PasswordKeyDerivation.SaltSizeBytes)
                {
                    return false;
                }

                derivedKey = PasswordKeyDerivation.DeriveKey(
                    password,
                    salt,
                    settings.Iterations,
                    AesGcmEncryption.KeySizeBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(salt);
            }

            SecretEnvelope envelope = settings.ProtectedDeploymentKey;
            var payload = new AesGcmPayload(
                Base64Url.Decode(envelope.Nonce),
                Base64Url.Decode(envelope.Tag),
                Base64Url.Decode(envelope.Ciphertext));
            deploymentKey = AesGcmEncryption.Decrypt(payload, derivedKey);
            if (deploymentKey.Length != AesGcmEncryption.KeySizeBytes)
            {
                return false;
            }

            session.SetKey(deploymentKey);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or CryptographicException)
        {
            return false;
        }
        finally
        {
            if (derivedKey is not null)
            {
                CryptographicOperations.ZeroMemory(derivedKey);
            }

            if (deploymentKey is not null)
            {
                CryptographicOperations.ZeroMemory(deploymentKey);
            }
        }
    }

    private static void ValidateSettings(DeployProtectionSettings settings)
    {
        SecretEnvelope envelope = settings.ProtectedDeploymentKey;
        if (!string.Equals(settings.KeyDerivationAlgorithm, KeyDerivationAlgorithm, StringComparison.Ordinal) ||
            settings.Iterations != PasswordKeyDerivation.DefaultIterations ||
            string.IsNullOrWhiteSpace(settings.Salt) ||
            !string.Equals(envelope.Kind, EnvelopeKind, StringComparison.Ordinal) ||
            !string.Equals(envelope.Algorithm, EnvelopeAlgorithm, StringComparison.Ordinal) ||
            !string.Equals(envelope.KeyId, PasswordDerivedKeyId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(envelope.Nonce) ||
            string.IsNullOrWhiteSpace(envelope.Tag) ||
            string.IsNullOrWhiteSpace(envelope.Ciphertext))
        {
            throw new CryptographicException("Deployment protection metadata is invalid.");
        }
    }
}
