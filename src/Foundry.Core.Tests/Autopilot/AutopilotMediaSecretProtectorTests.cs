// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;
using Foundry.Utilities.Security;

namespace Foundry.Core.Tests.Autopilot;

public sealed class AutopilotMediaSecretProtectorTests
{
    [Fact]
    public void EncryptBytes_DecryptBytes_RoundTripsBinaryPayload()
    {
        byte[] key = AesGcmEncryption.GenerateKey();
        byte[] payload = [0, 1, 2, 3, 255, 254, 128];

        var envelope = MediaSecretEnvelopeProtector.EncryptBytes(payload, key, MediaSecretEnvelopeProtector.DeploymentKeyId);
        byte[] decrypted = MediaSecretEnvelopeProtector.DecryptBytes(envelope, key, MediaSecretEnvelopeProtector.DeploymentKeyId);

        Assert.Equal(payload, decrypted);
        Assert.NotEqual(Convert.ToBase64String(payload), envelope.Ciphertext);
    }

    [Fact]
    public void DecryptBytes_WhenEnvelopeIsTampered_ThrowsWithoutLeakingSecretMaterial()
    {
        byte[] key = AesGcmEncryption.GenerateKey();
        const string secret = "PfxPassword-DoNotLeak";
        var envelope = MediaSecretEnvelopeProtector.EncryptString(secret, key, MediaSecretEnvelopeProtector.DeploymentKeyId) with
        {
            Ciphertext = "AAAA"
        };

        var exception = Assert.Throws<CryptographicException>(() => MediaSecretEnvelopeProtector.DecryptString(envelope, key, MediaSecretEnvelopeProtector.DeploymentKeyId));

        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(secret)), exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DecryptString_InvalidBase64_ThrowsCryptographicException()
    {
        byte[] key = AesGcmEncryption.GenerateKey();
        SecretEnvelope envelope = MediaSecretEnvelopeProtector.EncryptString("secret", key, MediaSecretEnvelopeProtector.DeploymentKeyId) with { Ciphertext = "!!" };
        CryptographicException ex = Assert.Throws<CryptographicException>(() => MediaSecretEnvelopeProtector.DecryptString(envelope, key, MediaSecretEnvelopeProtector.DeploymentKeyId));
        Assert.Contains("base64url", ex.Message);
    }

    [Fact]
    public void DecryptBytes_WithDeployKeyIds_RoundTripsDeploymentKeyedPayload()
    {
        byte[] key = RandomNumberGenerator.GetBytes(MediaSecretEnvelopeProtector.KeySizeBytes);
        byte[] payload = [0, 1, 2, 255, 128, 127];
        SecretEnvelope envelope = MediaSecretEnvelopeProtector.EncryptBytes(payload, key, MediaSecretEnvelopeProtector.DeploymentKeyId);

        byte[] decrypted = MediaSecretEnvelopeProtector.DecryptBytes(envelope, key, MediaSecretEnvelopeProtector.DeployKeyIds);

        Assert.Equal(payload, decrypted);
        Assert.NotEqual(Convert.ToBase64String(payload), envelope.Ciphertext);
    }

    [Fact]
    public void DecryptChars_WhenEnvelopeIsTampered_ThrowsWithoutLeakingSecret()
    {
        byte[] key = RandomNumberGenerator.GetBytes(MediaSecretEnvelopeProtector.KeySizeBytes);
        const string secret = "PfxPassword-DoNotLeak";
        SecretEnvelope envelope = MediaSecretEnvelopeProtector.EncryptString(secret, key, MediaSecretEnvelopeProtector.DeploymentKeyId) with
        {
            Ciphertext = "AAAA"
        };

        CryptographicException exception = Assert.Throws<CryptographicException>(
            () => MediaSecretEnvelopeProtector.DecryptChars(envelope, key, MediaSecretEnvelopeProtector.DeployKeyIds));

        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(secret)), exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DecryptChars_ReturnsClearableCharacterBuffer()
    {
        byte[] key = RandomNumberGenerator.GetBytes(MediaSecretEnvelopeProtector.KeySizeBytes);
        SecretEnvelope envelope = MediaSecretEnvelopeProtector.EncryptString("PfxPassword", key, MediaSecretEnvelopeProtector.DeploymentKeyId);

        char[] decrypted = MediaSecretEnvelopeProtector.DecryptChars(envelope, key, MediaSecretEnvelopeProtector.DeployKeyIds);

        Assert.Equal("PfxPassword", new string(decrypted));
        Assert.IsType<char[]>(decrypted);
    }

    [Theory]
    [InlineData("other")]
    [InlineData(MediaSecretEnvelopeProtector.KeyId)]
    public void DecryptChars_WithDeployKeyIds_RejectsUnexpectedKeyId(string keyId)
    {
        byte[] key = RandomNumberGenerator.GetBytes(MediaSecretEnvelopeProtector.KeySizeBytes);
        SecretEnvelope envelope = MediaSecretEnvelopeProtector.EncryptString("PfxPassword", key, keyId);

        CryptographicException exception = Assert.Throws<CryptographicException>(
            () => MediaSecretEnvelopeProtector.DecryptChars(envelope, key, MediaSecretEnvelopeProtector.DeployKeyIds));

        Assert.Equal("Encrypted secret envelope is not supported.", exception.Message);
    }

    [Fact]
    public void DecryptBytes_WithAcceptedKeyIds_WhenKeyLengthIsWrong_ThrowsArgumentException()
    {
        byte[] key = RandomNumberGenerator.GetBytes(MediaSecretEnvelopeProtector.KeySizeBytes);
        SecretEnvelope envelope = MediaSecretEnvelopeProtector.EncryptString("PfxPassword", key, MediaSecretEnvelopeProtector.DeploymentKeyId);

        Assert.Throws<ArgumentException>(
            () => MediaSecretEnvelopeProtector.DecryptBytes(envelope, new byte[5], MediaSecretEnvelopeProtector.DeployKeyIds));
    }

    [Fact]
    public void HasEncryptedSecrets_DetectsNestedEncryptedEnvelope()
    {
        byte[] key = AesGcmEncryption.GenerateKey();
        var envelope = MediaSecretEnvelopeProtector.EncryptString("secret", key, MediaSecretEnvelopeProtector.KeyId);
        string json = $$"""
            {
              "autopilot": {
                "certificatePasswordSecret": {
                  "kind": "{{envelope.Kind}}",
                  "algorithm": "{{envelope.Algorithm}}",
                  "keyId": "{{envelope.KeyId}}",
                  "nonce": "{{envelope.Nonce}}",
                  "tag": "{{envelope.Tag}}",
                  "ciphertext": "{{envelope.Ciphertext}}"
                }
              }
            }
            """;

        Assert.True(MediaSecretEnvelopeProtector.HasEncryptedSecrets(json));
    }
}
