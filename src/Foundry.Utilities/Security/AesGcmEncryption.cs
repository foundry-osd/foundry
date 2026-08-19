// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;

namespace Foundry.Utilities.Security;

public static class AesGcmEncryption
{
    public const int KeySizeBytes = 32;
    public const int NonceSizeBytes = 12;
    public const int TagSizeBytes = 16;

    public static byte[] GenerateKey()
    {
        return RandomNumberGenerator.GetBytes(KeySizeBytes);
    }

    public static AesGcmPayload Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key)
    {
        ValidateKey(key);

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        byte[] tag = new byte[TagSizeBytes];
        byte[] ciphertext = new byte[plaintext.Length];

        using AesGcm aes = new(key, TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return new AesGcmPayload(nonce, tag, ciphertext);
    }

    public static byte[] Decrypt(AesGcmPayload payload, ReadOnlySpan<byte> key)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ValidateKey(key);

        if (payload.Nonce is null || payload.Nonce.Length != NonceSizeBytes)
        {
            throw new CryptographicException($"The AES-GCM nonce must be {NonceSizeBytes} bytes.");
        }

        if (payload.Tag is null || payload.Tag.Length != TagSizeBytes)
        {
            throw new CryptographicException($"The AES-GCM authentication tag must be {TagSizeBytes} bytes.");
        }

        if (payload.Ciphertext is null)
        {
            throw new CryptographicException("The AES-GCM ciphertext is missing.");
        }

        byte[] plaintext = new byte[payload.Ciphertext.Length];
        try
        {
            using AesGcm aes = new(key, TagSizeBytes);
            aes.Decrypt(payload.Nonce, payload.Ciphertext, payload.Tag, plaintext);
            return plaintext;
        }
        catch (CryptographicException exception)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new CryptographicException("AES-GCM authentication failed.", exception);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
    }

    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySizeBytes)
        {
            throw new ArgumentException($"The AES-GCM key must be {KeySizeBytes} bytes.", nameof(key));
        }
    }
}
