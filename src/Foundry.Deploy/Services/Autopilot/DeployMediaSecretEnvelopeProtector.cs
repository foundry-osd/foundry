// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using Foundry.Deploy.Models.Configuration;
using Foundry.Utilities.Security;

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Decrypts the shared Foundry media secret envelope format inside WinPE without depending on Foundry.Core.
/// </summary>
public static class DeployMediaSecretEnvelopeProtector
{
    public const string Kind = "encrypted";
    public const string Algorithm = "aes-gcm-v1";
    public const string KeyId = "media";
    public const string DeploymentKeyId = "deployment";
    public const int KeySizeBytes = AesGcmEncryption.KeySizeBytes;

    public static byte[] DecryptBytes(SecretEnvelope envelope, byte[] key)
    {
        return DecryptBytes(envelope, key, KeyId);
    }

    public static byte[] DecryptBytes(SecretEnvelope envelope, byte[] key, string expectedKeyId)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateEnvelope(envelope, expectedKeyId);
        ValidateKey(key);

        try
        {
            return AesGcmEncryption.Decrypt(
                new AesGcmPayload(
                    Base64Url.Decode(envelope.Nonce),
                    Base64Url.Decode(envelope.Tag),
                    Base64Url.Decode(envelope.Ciphertext)),
                key);
        }
        catch (CryptographicException ex)
        {
            throw new CryptographicException("Encrypted secret could not be decrypted.", ex);
        }
    }

    public static string DecryptString(SecretEnvelope envelope, byte[] key)
    {
        byte[] plaintext = DecryptBytes(envelope, key);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static string DecryptString(SecretEnvelope envelope, byte[] key, string expectedKeyId)
    {
        byte[] plaintext = DecryptBytes(envelope, key, expectedKeyId);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static byte[] DecryptDeployBytes(SecretEnvelope envelope, byte[] key)
    {
        return DecryptBytes(envelope, key, ResolveDeployKeyId(envelope));
    }

    public static string DecryptDeployString(SecretEnvelope envelope, byte[] key)
    {
        return DecryptString(envelope, key, ResolveDeployKeyId(envelope));
    }

    private static string ResolveDeployKeyId(SecretEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (string.Equals(envelope.KeyId, DeploymentKeyId, StringComparison.Ordinal) ||
            string.Equals(envelope.KeyId, KeyId, StringComparison.Ordinal))
        {
            return envelope.KeyId;
        }

        throw new CryptographicException("Encrypted secret envelope is not supported.");
    }

    private static void ValidateEnvelope(SecretEnvelope envelope, string expectedKeyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedKeyId);
        if (!string.Equals(envelope.Kind, Kind, StringComparison.Ordinal) ||
            !string.Equals(envelope.Algorithm, Algorithm, StringComparison.Ordinal) ||
            !string.Equals(envelope.KeyId, expectedKeyId, StringComparison.Ordinal))
        {
            throw new CryptographicException("Encrypted secret envelope is not supported.");
        }

        if (string.IsNullOrWhiteSpace(envelope.Nonce) ||
            string.IsNullOrWhiteSpace(envelope.Tag) ||
            string.IsNullOrWhiteSpace(envelope.Ciphertext))
        {
            throw new CryptographicException("Encrypted secret envelope is incomplete.");
        }
    }

    private static void ValidateKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeySizeBytes)
        {
            throw new ArgumentException($"Media secret key must be {KeySizeBytes} bytes.", nameof(key));
        }
    }

}
