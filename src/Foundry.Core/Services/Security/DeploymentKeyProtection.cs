// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.WinPe;
using Foundry.Utilities.Security;

namespace Foundry.Core.Services.Security;

/// <summary>
/// Unwraps the per-media deployment key from the deployment password and authenticates a deployment key
/// handed over by another runtime, shared by Foundry Connect and Foundry Deploy.
/// </summary>
/// <remarks>
/// No member logs, and no exception message or return value carries password or key material. Every
/// intermediate buffer is zeroed before the member returns; returned keys are owned and zeroed by the caller.
/// </remarks>
public static class DeploymentKeyProtection
{
    /// <summary>
    /// Logical key identifier of the key-check envelope, distinct from the media and deployment secret key identifiers.
    /// </summary>
    public const string KeyCheckKeyId = "deployment-key-check";

    /// <summary>
    /// Fixed UTF-8 label encrypted under the deployment key; a candidate key is accepted only when it decrypts to it.
    /// </summary>
    private static ReadOnlySpan<byte> KeyCheckLabel => "foundry-deployment-key-check-v1"u8;

    /// <summary>
    /// Derives the password key with PBKDF2-SHA256 and decrypts the password-wrapped deployment key.
    /// </summary>
    /// <param name="keyDerivationAlgorithm">Key derivation algorithm identifier; must be <see cref="DeploymentMediaProtectionService.KeyDerivationAlgorithm"/>.</param>
    /// <param name="iterations">PBKDF2 iteration count; must equal <see cref="PasswordKeyDerivation.DefaultIterations"/>.</param>
    /// <param name="salt">Unpadded Base64URL salt of <see cref="PasswordKeyDerivation.SaltSizeBytes"/> bytes.</param>
    /// <param name="protectedDeploymentKey">Envelope with key identifier <see cref="DeploymentMediaProtectionService.PasswordDerivedKeyId"/>.</param>
    /// <param name="password">Deployment password typed by the technician.</param>
    /// <returns>
    /// The 32-byte deployment key, which the caller must zero; <see langword="null"/> for a wrong password or
    /// any unsupported, incomplete or malformed metadata.
    /// </returns>
    public static byte[]? TryUnwrap(
        string? keyDerivationAlgorithm,
        int iterations,
        string? salt,
        SecretEnvelope? protectedDeploymentKey,
        ReadOnlySpan<char> password)
    {
        if (!IsSupported(keyDerivationAlgorithm, iterations, salt, protectedDeploymentKey))
        {
            return null;
        }

        byte[]? derivedKey = null;
        byte[]? deploymentKey = null;
        try
        {
            derivedKey = DerivePasswordKey(password, salt!);
            if (derivedKey is null)
            {
                return null;
            }

            deploymentKey = MediaSecretEnvelopeProtector.DecryptBytes(
                protectedDeploymentKey!,
                derivedKey,
                DeploymentMediaProtectionService.PasswordDerivedKeyId);
            if (deploymentKey.Length != AesGcmEncryption.KeySizeBytes)
            {
                return null;
            }

            byte[] result = deploymentKey;
            deploymentKey = null;
            return result;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or CryptographicException)
        {
            return null;
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

    /// <summary>
    /// Unwraps the deployment key from generated Deploy protection settings.
    /// </summary>
    /// <param name="settings">Protection block of the generated Deploy configuration.</param>
    /// <param name="password">Deployment password typed by the technician.</param>
    /// <returns>The deployment key, which the caller must zero, or <see langword="null"/>.</returns>
    public static byte[]? TryUnwrap(DeployProtectionSettings settings, ReadOnlySpan<char> password)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return TryUnwrap(
            settings.KeyDerivationAlgorithm,
            settings.Iterations,
            settings.Salt,
            settings.ProtectedDeploymentKey,
            password);
    }

    /// <summary>
    /// Creates the key-check envelope: the fixed label encrypted under the deployment key with <see cref="KeyCheckKeyId"/>.
    /// </summary>
    /// <param name="deploymentKey">The 32-byte deployment key of the media being created.</param>
    /// <returns>An envelope that authenticates the deployment key without revealing it.</returns>
    public static SecretEnvelope CreateKeyCheck(byte[] deploymentKey)
    {
        return MediaSecretEnvelopeProtector.EncryptBytes(KeyCheckLabel.ToArray(), deploymentKey, KeyCheckKeyId);
    }

    /// <summary>
    /// Determines whether a candidate key is this media's deployment key.
    /// </summary>
    /// <param name="keyCheck">Key-check envelope of the media; absent on media built before the key check.</param>
    /// <param name="candidateKey">Key to authenticate; not modified.</param>
    /// <returns>
    /// <see langword="true"/> only when the envelope authenticates under <paramref name="candidateKey"/> and decrypts
    /// to the fixed label; <see langword="false"/> otherwise. Never throws.
    /// </returns>
    public static bool VerifyKeyCheck(SecretEnvelope? keyCheck, byte[] candidateKey)
    {
        if (keyCheck is null || candidateKey is null || candidateKey.Length != AesGcmEncryption.KeySizeBytes)
        {
            return false;
        }

        byte[]? label = null;
        try
        {
            label = MediaSecretEnvelopeProtector.DecryptBytes(keyCheck, candidateKey, KeyCheckKeyId);
            return CryptographicOperations.FixedTimeEquals(label, KeyCheckLabel);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or CryptographicException)
        {
            return false;
        }
        finally
        {
            if (label is not null)
            {
                CryptographicOperations.ZeroMemory(label);
            }
        }
    }

    private static bool IsSupported(
        string? keyDerivationAlgorithm,
        int iterations,
        string? salt,
        SecretEnvelope? protectedDeploymentKey)
    {
        return protectedDeploymentKey is not null &&
            string.Equals(keyDerivationAlgorithm, DeploymentMediaProtectionService.KeyDerivationAlgorithm, StringComparison.Ordinal) &&
            iterations == PasswordKeyDerivation.DefaultIterations &&
            !string.IsNullOrWhiteSpace(salt) &&
            string.Equals(protectedDeploymentKey.Kind, DeploymentMediaProtectionService.EnvelopeKind, StringComparison.Ordinal) &&
            string.Equals(protectedDeploymentKey.Algorithm, DeploymentMediaProtectionService.EnvelopeAlgorithm, StringComparison.Ordinal) &&
            string.Equals(protectedDeploymentKey.KeyId, DeploymentMediaProtectionService.PasswordDerivedKeyId, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(protectedDeploymentKey.Nonce) &&
            !string.IsNullOrWhiteSpace(protectedDeploymentKey.Tag) &&
            !string.IsNullOrWhiteSpace(protectedDeploymentKey.Ciphertext);
    }

    private static byte[]? DerivePasswordKey(ReadOnlySpan<char> password, string salt)
    {
        byte[] saltBytes = Base64Url.Decode(salt);
        try
        {
            return saltBytes.Length == PasswordKeyDerivation.SaltSizeBytes
                ? PasswordKeyDerivation.DeriveKey(password, saltBytes, PasswordKeyDerivation.DefaultIterations, AesGcmEncryption.KeySizeBytes)
                : null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(saltBytes);
        }
    }
}
