// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Autopilot;

/// <summary>
/// Protects media-embedded secrets with the shared Foundry AES-GCM envelope format.
/// </summary>
public static class MediaSecretEnvelopeProtector
{
    /// <summary>
    /// Envelope kind used for media-embedded encrypted secrets.
    /// </summary>
    public const string Kind = "encrypted";

    /// <summary>
    /// Encryption algorithm identifier serialized into media secret envelopes.
    /// </summary>
    public const string Algorithm = "aes-gcm-v1";

    /// <summary>
    /// Logical key identifier for the media secret key stored with generated boot media.
    /// </summary>
    public const string KeyId = "media";

    /// <summary>
    /// Required AES-256 media secret key length.
    /// </summary>
    public const int KeySizeBytes = 32;

    /// <summary>
    /// Required AES-GCM nonce length.
    /// </summary>
    public const int NonceSizeBytes = 12;

    /// <summary>
    /// Required AES-GCM authentication tag length.
    /// </summary>
    public const int TagSizeBytes = 16;

    /// <summary>
    /// Creates a new random media secret key for generated boot media.
    /// </summary>
    /// <returns>A 32-byte media secret key.</returns>
    public static byte[] GenerateMediaKey()
    {
        byte[] key = new byte[KeySizeBytes];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    /// <summary>
    /// Encrypts a UTF-8 string into the shared media secret envelope format.
    /// </summary>
    /// <param name="plaintext">Plaintext value to encrypt.</param>
    /// <param name="key">Media secret key.</param>
    /// <returns>Encrypted secret envelope safe to serialize into generated configuration.</returns>
    public static SecretEnvelope EncryptString(string plaintext, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            return EncryptBytes(plaintextBytes, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    /// <summary>
    /// Decrypts a media secret envelope into a UTF-8 string.
    /// </summary>
    /// <param name="envelope">Encrypted secret envelope.</param>
    /// <param name="key">Media secret key.</param>
    /// <returns>Decrypted string value.</returns>
    public static string DecryptString(SecretEnvelope envelope, byte[] key)
    {
        byte[] plaintextBytes = DecryptBytes(envelope, key);
        try
        {
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    /// <summary>
    /// Encrypts binary secret material into the shared media secret envelope format.
    /// </summary>
    /// <param name="plaintext">Plaintext bytes to encrypt.</param>
    /// <param name="key">Media secret key.</param>
    /// <returns>Encrypted secret envelope safe to serialize into generated configuration.</returns>
    public static SecretEnvelope EncryptBytes(byte[] plaintext, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ValidateKey(key);

        byte[] nonce = new byte[NonceSizeBytes];
        byte[] tag = new byte[TagSizeBytes];
        byte[] ciphertext = new byte[plaintext.Length];

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

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

    /// <summary>
    /// Decrypts binary secret material from a media secret envelope.
    /// </summary>
    /// <param name="envelope">Encrypted secret envelope.</param>
    /// <param name="key">Media secret key.</param>
    /// <returns>Decrypted bytes. The caller is responsible for zeroing the returned buffer.</returns>
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

    /// <summary>
    /// Detects whether serialized JSON contains at least one shared media secret envelope.
    /// </summary>
    /// <param name="json">Serialized configuration JSON.</param>
    /// <returns><see langword="true"/> when an encrypted media secret envelope is present.</returns>
    public static bool HasEncryptedSecrets(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        return HasEncryptedSecrets(document.RootElement);
    }

    /// <summary>
    /// Determines whether a JSON object matches the shared media secret envelope shape.
    /// </summary>
    /// <param name="element">JSON element to inspect.</param>
    /// <returns><see langword="true"/> when the element is a complete encrypted media secret envelope.</returns>
    public static bool IsEncryptedSecretEnvelope(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Object &&
               HasStringProperty(element, "kind", Kind) &&
               HasStringProperty(element, "algorithm", Algorithm) &&
               HasStringProperty(element, "keyId", KeyId) &&
               HasNonEmptyStringProperty(element, "nonce") &&
               HasNonEmptyStringProperty(element, "tag") &&
               HasNonEmptyStringProperty(element, "ciphertext");
    }

    private static bool HasEncryptedSecrets(JsonElement element)
    {
        if (IsEncryptedSecretEnvelope(element))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (HasEncryptedSecrets(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (HasEncryptedSecrets(item))
                {
                    return true;
                }
            }
        }

        return false;
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

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        string base64 = value.Replace('-', '+').Replace('_', '/');
        int padding = (4 - base64.Length % 4) % 4;
        base64 = base64.PadRight(base64.Length + padding, '=');
        return Convert.FromBase64String(base64);
    }

    private static bool HasStringProperty(JsonElement element, string propertyName, string expectedValue)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
               property.ValueKind == JsonValueKind.String &&
               string.Equals(property.GetString(), expectedValue, StringComparison.Ordinal);
    }

    private static bool HasNonEmptyStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
               property.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(property.GetString());
    }
}
