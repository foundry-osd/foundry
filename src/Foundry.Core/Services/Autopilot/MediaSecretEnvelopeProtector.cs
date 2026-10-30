// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Utilities.Security;

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
    /// Logical key identifier for the media secret key, which exists on older (schema 5) media only.
    /// </summary>
    public const string KeyId = "media";

    /// <summary>
    /// Logical key identifier for Deploy-only secrets.
    /// </summary>
    public const string DeploymentKeyId = "deployment";

    /// <summary>
    /// Logical key identifiers Deploy accepts for the secrets it decrypts at runtime: the Deploy-only
    /// <see cref="DeploymentKeyId"/> and the shared <see cref="KeyId"/> media key identifier.
    /// </summary>
    public static readonly IReadOnlyCollection<string> DeployKeyIds = [DeploymentKeyId, KeyId];

    /// <summary>
    /// Required AES-256 media secret key length.
    /// </summary>
    public const int KeySizeBytes = AesGcmEncryption.KeySizeBytes;

    /// <summary>
    /// Required AES-GCM nonce length.
    /// </summary>
    public const int NonceSizeBytes = AesGcmEncryption.NonceSizeBytes;

    /// <summary>
    /// Required AES-GCM authentication tag length.
    /// </summary>
    public const int TagSizeBytes = AesGcmEncryption.TagSizeBytes;

    /// <summary>
    /// Creates a new random 32-byte key. Generated media no longer store a media key; one exists on older (schema 5) media only.
    /// </summary>
    /// <returns>A 32-byte key.</returns>
    public static byte[] GenerateMediaKey()
    {
        return AesGcmEncryption.GenerateKey();
    }

    /// <summary>
    /// Encrypts a UTF-8 string into the shared media secret envelope format.
    /// </summary>
    /// <param name="plaintext">Plaintext value to encrypt.</param>
    /// <param name="key">Media secret key.</param>
    /// <returns>Encrypted secret envelope safe to serialize into generated configuration.</returns>
    public static SecretEnvelope EncryptString(string plaintext, byte[] key)
    {
        return EncryptString(plaintext, key, KeyId);
    }

    /// <summary>
    /// Encrypts a UTF-8 string with an explicit logical key identifier.
    /// </summary>
    public static SecretEnvelope EncryptString(string plaintext, byte[] key, string keyId)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            return EncryptBytes(plaintextBytes, key, keyId);
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
        return DecryptString(envelope, key, KeyId);
    }

    /// <summary>
    /// Decrypts a UTF-8 string with an explicit logical key identifier.
    /// </summary>
    public static string DecryptString(SecretEnvelope envelope, byte[] key, string keyId)
    {
        byte[] plaintextBytes = DecryptBytes(envelope, key, keyId);
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
        return EncryptBytes(plaintext, key, KeyId);
    }

    /// <summary>
    /// Encrypts binary secret material with an explicit logical key identifier.
    /// </summary>
    public static SecretEnvelope EncryptBytes(byte[] plaintext, byte[] key, string keyId)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ValidateKey(key);
        ValidateKeyId(keyId);

        AesGcmPayload payload = AesGcmEncryption.Encrypt(plaintext, key);

        return new SecretEnvelope
        {
            Kind = Kind,
            Algorithm = Algorithm,
            KeyId = keyId,
            Nonce = Base64Url.Encode(payload.Nonce),
            Tag = Base64Url.Encode(payload.Tag),
            Ciphertext = Base64Url.Encode(payload.Ciphertext)
        };
    }

    /// <summary>
    /// Decrypts binary secret material from a media secret envelope.
    /// </summary>
    /// <param name="envelope">Encrypted secret envelope.</param>
    /// <param name="key">Media secret key.</param>
    /// <returns>Decrypted bytes. The caller is responsible for zeroing the returned buffer.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> is not <see cref="KeySizeBytes"/> bytes long.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The envelope is unsupported, incomplete, not valid base64url, or fails authentication.
    /// </exception>
    public static byte[] DecryptBytes(SecretEnvelope envelope, byte[] key)
    {
        return DecryptBytes(envelope, key, KeyId);
    }

    /// <summary>
    /// Decrypts binary secret material with an explicit logical key identifier.
    /// </summary>
    /// <param name="envelope">Encrypted secret envelope.</param>
    /// <param name="key">Media secret key.</param>
    /// <param name="keyId">Logical key identifier the envelope must carry.</param>
    /// <returns>Decrypted bytes. The caller is responsible for zeroing the returned buffer.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> is not <see cref="KeySizeBytes"/> bytes long, or <paramref name="keyId"/> is empty.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The envelope is unsupported, incomplete, not valid base64url, or fails authentication.
    /// </exception>
    public static byte[] DecryptBytes(SecretEnvelope envelope, byte[] key, string keyId)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateKeyId(keyId);
        ValidateEnvelope(envelope, keyId);
        ValidateKey(key);

        AesGcmPayload payload;
        try
        {
            payload = new AesGcmPayload(
                Base64Url.Decode(envelope.Nonce),
                Base64Url.Decode(envelope.Tag),
                Base64Url.Decode(envelope.Ciphertext));
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Encrypted secret envelope contains invalid base64url data.", ex);
        }

        try
        {
            return AesGcmEncryption.Decrypt(payload, key);
        }
        catch (CryptographicException ex)
        {
            throw new CryptographicException("Encrypted secret could not be decrypted.", ex);
        }
    }

    /// <summary>
    /// Decrypts binary secret material from an envelope whose logical key identifier is one of the accepted identifiers.
    /// </summary>
    /// <param name="envelope">Encrypted secret envelope.</param>
    /// <param name="key">Media secret key.</param>
    /// <param name="acceptedKeyIds">Logical key identifiers the envelope may carry, compared ordinally.</param>
    /// <returns>Decrypted bytes. The caller is responsible for zeroing the returned buffer.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> is not <see cref="KeySizeBytes"/> bytes long, or <paramref name="acceptedKeyIds"/>
    /// is empty or contains an empty key identifier.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The envelope key identifier is not accepted, or the envelope is unsupported, incomplete,
    /// not valid base64url, or fails authentication.
    /// </exception>
    public static byte[] DecryptBytes(SecretEnvelope envelope, byte[] key, IReadOnlyCollection<string> acceptedKeyIds)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateKeyIds(acceptedKeyIds);

        return DecryptBytes(envelope, key, ResolveAcceptedKeyId(envelope, acceptedKeyIds));
    }

    /// <summary>
    /// Decrypts a UTF-8 secret into a character buffer the caller can clear, without creating a managed string.
    /// </summary>
    /// <param name="envelope">Encrypted secret envelope.</param>
    /// <param name="key">Media secret key.</param>
    /// <param name="acceptedKeyIds">Logical key identifiers the envelope may carry, compared ordinally.</param>
    /// <returns>Decrypted characters. The caller is responsible for clearing the returned buffer.</returns>
    /// <remarks>
    /// The intermediate plaintext byte buffer is zeroed before this method returns, including when decoding fails.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> is not <see cref="KeySizeBytes"/> bytes long, or <paramref name="acceptedKeyIds"/>
    /// is empty or contains an empty key identifier.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The envelope key identifier is not accepted, or the envelope is unsupported, incomplete,
    /// not valid base64url, or fails authentication.
    /// </exception>
    public static char[] DecryptChars(SecretEnvelope envelope, byte[] key, IReadOnlyCollection<string> acceptedKeyIds)
    {
        byte[] plaintext = DecryptBytes(envelope, key, acceptedKeyIds);
        try
        {
            char[] characters = new char[Encoding.UTF8.GetCharCount(plaintext)];
            Encoding.UTF8.GetChars(plaintext, characters);
            return characters;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
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

    private static void ValidateEnvelope(SecretEnvelope envelope, string keyId)
    {
        if (!string.Equals(envelope.Kind, Kind, StringComparison.Ordinal) ||
            !string.Equals(envelope.Algorithm, Algorithm, StringComparison.Ordinal) ||
            !string.Equals(envelope.KeyId, keyId, StringComparison.Ordinal))
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

    private static string ResolveAcceptedKeyId(SecretEnvelope envelope, IReadOnlyCollection<string> acceptedKeyIds)
    {
        foreach (string acceptedKeyId in acceptedKeyIds)
        {
            if (string.Equals(envelope.KeyId, acceptedKeyId, StringComparison.Ordinal))
            {
                return acceptedKeyId;
            }
        }

        throw new CryptographicException("Encrypted secret envelope is not supported.");
    }

    private static void ValidateKeyIds(IReadOnlyCollection<string> acceptedKeyIds)
    {
        ArgumentNullException.ThrowIfNull(acceptedKeyIds);
        if (acceptedKeyIds.Count == 0)
        {
            throw new ArgumentException("At least one accepted key identifier is required.", nameof(acceptedKeyIds));
        }

        foreach (string acceptedKeyId in acceptedKeyIds)
        {
            ValidateKeyId(acceptedKeyId);
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

    private static void ValidateKeyId(string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId))
        {
            throw new ArgumentException("Encrypted secret key identifier is required.", nameof(keyId));
        }
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
