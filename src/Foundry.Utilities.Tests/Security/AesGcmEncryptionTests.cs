// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Utilities.Security;

namespace Foundry.Utilities.Tests.Security;

public sealed class AesGcmEncryptionTests
{
    [Fact]
    public void EncryptDecrypt_RoundTripsBinaryPayload()
    {
        byte[] key = AesGcmEncryption.GenerateKey();
        byte[] plaintext = "Foundry deployment secret"u8.ToArray();

        AesGcmPayload payload = AesGcmEncryption.Encrypt(plaintext, key);
        byte[] result = AesGcmEncryption.Decrypt(payload, key);

        Assert.Equal(plaintext, result);
        Assert.Equal(AesGcmEncryption.NonceSizeBytes, payload.Nonce.Length);
        Assert.Equal(AesGcmEncryption.TagSizeBytes, payload.Tag.Length);
    }

    [Fact]
    public void Decrypt_WhenCiphertextIsModified_ThrowsCryptographicException()
    {
        byte[] key = AesGcmEncryption.GenerateKey();
        AesGcmPayload payload = AesGcmEncryption.Encrypt("secret"u8, key);
        payload.Ciphertext[0] ^= 0x01;

        Assert.Throws<CryptographicException>(() => AesGcmEncryption.Decrypt(payload, key));
    }

    [Fact]
    public void Encrypt_WithNon32ByteKey_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => AesGcmEncryption.Encrypt("secret"u8, new byte[16]));
    }

    [Fact]
    public void Decrypt_WithInvalidNonceLength_ThrowsCryptographicException()
    {
        byte[] key = AesGcmEncryption.GenerateKey();
        AesGcmPayload payload = new(new byte[11], new byte[16], new byte[6]);

        Assert.Throws<CryptographicException>(() => AesGcmEncryption.Decrypt(payload, key));
    }
}
