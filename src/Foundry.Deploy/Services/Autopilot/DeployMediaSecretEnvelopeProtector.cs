// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using Foundry.Deploy.Models.Configuration;

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Decrypts the shared Foundry media secret envelope format inside WinPE without depending on Foundry.Core.
/// </summary>
public static class DeployMediaSecretEnvelopeProtector
{
    public const string Kind = "encrypted";
    public const string Algorithm = "aes-gcm-v1";
    public const string KeyId = "media";
    public const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    public static byte[] DecryptBytes(SecretEnvelope envelope, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateEnvelope(envelope);
        ValidateKey(key);

        byte[] nonce = Base64UrlDecode(envelope.Nonce);
        byte[] tag = Base64UrlDecode(envelope.Tag);
        byte[] ciphertext = Base64UrlDecode(envelope.Ciphertext);
        byte[] plaintext = new byte[ciphertext.Length];

        if (nonce.Length != NonceSizeBytes)
        {
            throw new CryptographicException("Encrypted secret nonce has an invalid length.");
        }

        if (tag.Length != TagSizeBytes)
        {
            throw new CryptographicException("Encrypted secret tag has an invalid length.");
        }

        try
        {
            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(plaintext);
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

    private static void ValidateEnvelope(SecretEnvelope envelope)
    {
        if (!string.Equals(envelope.Kind, Kind, StringComparison.Ordinal) ||
            !string.Equals(envelope.Algorithm, Algorithm, StringComparison.Ordinal) ||
            !string.Equals(envelope.KeyId, KeyId, StringComparison.Ordinal))
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

    private static byte[] Base64UrlDecode(string value)
    {
        string base64 = value.Replace('-', '+').Replace('_', '/');
        int padding = (4 - base64.Length % 4) % 4;
        return Convert.FromBase64String(base64.PadRight(base64.Length + padding, '='));
    }
}
