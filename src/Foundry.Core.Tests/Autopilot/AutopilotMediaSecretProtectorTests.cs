using System.Security.Cryptography;
using System.Text;
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
