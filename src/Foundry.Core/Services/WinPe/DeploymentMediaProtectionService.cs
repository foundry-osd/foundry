// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Utilities.Security;

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Creates per-media Deploy keys and optional password-wrapping metadata.
/// </summary>
public static class DeploymentMediaProtectionService
{
    public const string KeyDerivationAlgorithm = "pbkdf2-sha256";
    public const string EnvelopeKind = "encrypted";
    public const string EnvelopeAlgorithm = "aes-gcm-v1";
    public const string PasswordDerivedKeyId = "deployment-password";

    public static DeploymentMediaProtectionMaterial CreateProtected(ReadOnlySpan<char> password)
    {
        if (password.Length < Configuration.DeploymentProtectionPasswordRules.MinimumLength)
        {
            throw new ArgumentException(
                $"The deployment media password must contain at least {Configuration.DeploymentProtectionPasswordRules.MinimumLength} characters.",
                nameof(password));
        }

        byte[] salt = PasswordKeyDerivation.GenerateSalt();
        byte[] derivedKey = PasswordKeyDerivation.DeriveKey(
            password,
            salt,
            PasswordKeyDerivation.DefaultIterations,
            AesGcmEncryption.KeySizeBytes);
        byte[] deploymentKey = AesGcmEncryption.GenerateKey();

        try
        {
            AesGcmPayload payload = AesGcmEncryption.Encrypt(deploymentKey, derivedKey);
            var envelope = new SecretEnvelope
            {
                Kind = EnvelopeKind,
                Algorithm = EnvelopeAlgorithm,
                KeyId = PasswordDerivedKeyId,
                Nonce = Base64Url.Encode(payload.Nonce),
                Tag = Base64Url.Encode(payload.Tag),
                Ciphertext = Base64Url.Encode(payload.Ciphertext)
            };
            var settings = new DeployProtectionSettings
            {
                IsEnabled = true,
                KeyDerivationAlgorithm = KeyDerivationAlgorithm,
                Iterations = PasswordKeyDerivation.DefaultIterations,
                Salt = Base64Url.Encode(salt),
                ProtectedDeploymentKey = envelope
            };

            return new DeploymentMediaProtectionMaterial(deploymentKey, settings);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(deploymentKey);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derivedKey);
            CryptographicOperations.ZeroMemory(salt);
        }
    }

    public static DeploymentMediaProtectionMaterial CreateUnprotected()
    {
        return new DeploymentMediaProtectionMaterial(
            AesGcmEncryption.GenerateKey(),
            new DeployProtectionSettings());
    }
}
