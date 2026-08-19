// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using Foundry.Connect.Models.Configuration;
using Foundry.Utilities.Security;

namespace Foundry.Connect.Services.Configuration;

internal static class ConnectSecretEnvelopeProtector
{
    private const string Kind = "encrypted";
    private const string Algorithm = "aes-gcm-v1";
    private const string KeyId = "media";
    private const int KeySizeBytes = AesGcmEncryption.KeySizeBytes;

    public static string Decrypt(SecretEnvelope envelope, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateEnvelope(envelope);
        ValidateKey(key);

        byte[]? plaintext = null;

        try
        {
            plaintext = AesGcmEncryption.Decrypt(
                new AesGcmPayload(
                    Base64Url.Decode(envelope.Nonce),
                    Base64Url.Decode(envelope.Tag),
                    Base64Url.Decode(envelope.Ciphertext)),
                key);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (FormatException ex)
        {
            throw new FoundryConnectConfigurationException("Encrypted secret envelope contains invalid base64url data.", ex);
        }
        catch (CryptographicException ex)
        {
            throw new FoundryConnectConfigurationException("Encrypted secret could not be decrypted.", ex);
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
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

}
