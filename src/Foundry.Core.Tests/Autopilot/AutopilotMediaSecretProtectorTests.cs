// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;

namespace Foundry.Core.Tests.Autopilot;

public sealed class AutopilotMediaSecretProtectorTests
{
    [Fact]
    public void EncryptBytes_DecryptBytes_RoundTripsBinaryPayload()
    {
        byte[] key = MediaSecretEnvelopeProtector.GenerateMediaKey();
        byte[] payload = [0, 1, 2, 3, 255, 254, 128];

        var envelope = MediaSecretEnvelopeProtector.EncryptBytes(payload, key);
        byte[] decrypted = MediaSecretEnvelopeProtector.DecryptBytes(envelope, key);

        Assert.Equal(payload, decrypted);
        Assert.NotEqual(Convert.ToBase64String(payload), envelope.Ciphertext);
    }

    [Fact]
    public void DecryptBytes_WhenEnvelopeIsTampered_ThrowsWithoutLeakingSecretMaterial()
    {
        byte[] key = MediaSecretEnvelopeProtector.GenerateMediaKey();
        const string secret = "PfxPassword-DoNotLeak";
        var envelope = MediaSecretEnvelopeProtector.EncryptString(secret, key) with
        {
            Ciphertext = "AAAA"
        };

        var exception = Assert.Throws<CryptographicException>(() => MediaSecretEnvelopeProtector.DecryptString(envelope, key));

        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(secret)), exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DecryptString_InvalidBase64_ThrowsCryptographicException()
    {
        byte[] key = MediaSecretEnvelopeProtector.GenerateMediaKey();
        SecretEnvelope envelope = MediaSecretEnvelopeProtector.EncryptString("secret", key) with { Ciphertext = "!!" };
        CryptographicException ex = Assert.Throws<CryptographicException>(() => MediaSecretEnvelopeProtector.DecryptString(envelope, key));
        Assert.Contains("base64url", ex.Message);
    }

    [Theory]
    [InlineData(MediaSecretEnvelopeProtector.KeyId)]
    [InlineData(MediaSecretEnvelopeProtector.DeploymentKeyId)]
    public void DecryptBytes_WithDeployKeyIds_RoundTripsBinaryPayloadForEveryAcceptedKeyId(string keyId)
    {
        byte[] key = RandomNumberGenerator.GetBytes(MediaSecretEnvelopeProtector.KeySizeBytes);
        byte[] payload = [0, 1, 2, 255, 128, 127];
        SecretEnvelope envelope = MediaSecretEnvelopeProtector.EncryptBytes(payload, key, keyId);

        byte[] decrypted = MediaSecretEnvelopeProtector.DecryptBytes(envelope, key, MediaSecretEnvelopeProtector.DeployKeyIds);

        Assert.Equal(payload, decrypted);
        Assert.NotEqual(Convert.ToBase64String(payload), envelope.Ciphertext);
    }

    [Fact]
    public void DecryptChars_WhenEnvelopeIsTampered_ThrowsWithoutLeakingSecret()
    {
        byte[] key = RandomNumberGenerator.GetBytes(MediaSecretEnvelopeProtector.KeySizeBytes);
        const string secret = "PfxPassword-DoNotLeak";
        SecretEnvelope envelope = MediaSecretEnvelopeProtector.EncryptString(secret, key) with
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
        SecretEnvelope envelope = MediaSecretEnvelopeProtector.EncryptString("PfxPassword", key);

        char[] decrypted = MediaSecretEnvelopeProtector.DecryptChars(envelope, key, MediaSecretEnvelopeProtector.DeployKeyIds);

        Assert.Equal("PfxPassword", new string(decrypted));
        Assert.IsType<char[]>(decrypted);
    }

    [Fact]
    public void DecryptChars_RejectsUnexpectedKeyId()
    {
        byte[] key = RandomNumberGenerator.GetBytes(MediaSecretEnvelopeProtector.KeySizeBytes);
        SecretEnvelope envelope = MediaSecretEnvelopeProtector.EncryptString("PfxPassword", key, "other");

        CryptographicException exception = Assert.Throws<CryptographicException>(
            () => MediaSecretEnvelopeProtector.DecryptChars(envelope, key, MediaSecretEnvelopeProtector.DeployKeyIds));

        Assert.Equal("Encrypted secret envelope is not supported.", exception.Message);
    }

    [Fact]
    public void DecryptBytes_WithAcceptedKeyIds_WhenKeyLengthIsWrong_ThrowsArgumentException()
    {
        byte[] key = RandomNumberGenerator.GetBytes(MediaSecretEnvelopeProtector.KeySizeBytes);
        SecretEnvelope envelope = MediaSecretEnvelopeProtector.EncryptString("PfxPassword", key);

        Assert.Throws<ArgumentException>(
            () => MediaSecretEnvelopeProtector.DecryptBytes(envelope, new byte[5], MediaSecretEnvelopeProtector.DeployKeyIds));
    }

    [Fact]
    public void HasEncryptedSecrets_DetectsNestedEncryptedEnvelope()
    {
        byte[] key = MediaSecretEnvelopeProtector.GenerateMediaKey();
        var envelope = MediaSecretEnvelopeProtector.EncryptString("secret", key);
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
