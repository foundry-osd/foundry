using System.Security.Cryptography;
using System.Text;
using Foundry.Connect.Models.Configuration;

namespace Foundry.Connect.Services.Configuration;

internal static class ConnectSecretEnvelopeProtector
{
    private const string Kind = "encrypted";
    private const string Algorithm = "aes-gcm-v1";
    private const string KeyId = "media";
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    public static string Decrypt(SecretEnvelope envelope, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateEnvelope(envelope);
        ValidateKey(key);

        byte[] nonce = Base64UrlDecode(envelope.Nonce);
        byte[] tag = Base64UrlDecode(envelope.Tag);
        byte[] ciphertext = Base64UrlDecode(envelope.Ciphertext);
        byte[] plaintext = new byte[ciphertext.Length];

        try
        {
            if (nonce.Length != NonceSizeBytes)
            {
                throw new FoundryConnectConfigurationException("Encrypted secret nonce has an invalid length.");
            }

            if (tag.Length != TagSizeBytes)
            {
                throw new FoundryConnectConfigurationException("Encrypted secret tag has an invalid length.");
            }

            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException ex)
        {
            throw new FoundryConnectConfigurationException("Encrypted secret could not be decrypted.", ex);
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
            throw new FoundryConnectConfigurationException("Encrypted secret envelope is not supported.");
        }

        if (string.IsNullOrWhiteSpace(envelope.Nonce) ||
            string.IsNullOrWhiteSpace(envelope.Tag) ||
            string.IsNullOrWhiteSpace(envelope.Ciphertext))
        {
            throw new FoundryConnectConfigurationException("Encrypted secret envelope is incomplete.");
        }
    }

    private static void ValidateKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeySizeBytes)
        {
            throw new FoundryConnectConfigurationException("Media secret key has an invalid length.");
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        string base64 = value.Replace('-', '+').Replace('_', '/');
        int padding = (4 - base64.Length % 4) % 4;
        base64 = base64.PadRight(base64.Length + padding, '=');

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException ex)
        {
            throw new FoundryConnectConfigurationException("Encrypted secret envelope contains invalid base64url data.", ex);
        }
    }
}
