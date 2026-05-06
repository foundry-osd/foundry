using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

internal static class ConnectSecretEnvelopeProtector
{
    public const string Kind = "encrypted";
    public const string Algorithm = "aes-gcm-v1";
    public const string KeyId = "media";
    public const int KeySizeBytes = 32;
    public const int NonceSizeBytes = 12;
    public const int TagSizeBytes = 16;

    public static byte[] GenerateMediaKey()
    {
        byte[] key = new byte[KeySizeBytes];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    public static SecretEnvelope Encrypt(string plaintext, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ValidateKey(key);

        byte[] nonce = new byte[NonceSizeBytes];
        byte[] tag = new byte[TagSizeBytes];
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] ciphertext = new byte[plaintextBytes.Length];

        try
        {
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

            return new SecretEnvelope
            {
                Kind = Kind,
                Algorithm = Algorithm,
                KeyId = KeyId,
                Nonce = Base64UrlEncode(nonce),
                Tag = Base64UrlEncode(tag),
                Ciphertext = Base64UrlEncode(ciphertext)
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
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

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
