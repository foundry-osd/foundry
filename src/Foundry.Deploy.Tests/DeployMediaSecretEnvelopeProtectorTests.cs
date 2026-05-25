using System.Security.Cryptography;
using System.Text;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Autopilot;

namespace Foundry.Deploy.Tests;

public sealed class DeployMediaSecretEnvelopeProtectorTests
{
    [Fact]
    public void DecryptBytes_RoundTripsBinaryPayloadWithoutWritingSecretFile()
    {
        byte[] key = RandomNumberGenerator.GetBytes(DeployMediaSecretEnvelopeProtector.KeySizeBytes);
        byte[] payload = [0, 1, 2, 255, 128, 127];
        SecretEnvelope envelope = Encrypt(payload, key);

        byte[] decrypted = DeployMediaSecretEnvelopeProtector.DecryptBytes(envelope, key);

        Assert.Equal(payload, decrypted);
        Assert.NotEqual(Convert.ToBase64String(payload), envelope.Ciphertext);
    }

    [Fact]
    public void DecryptString_WhenEnvelopeIsTampered_ThrowsWithoutLeakingSecret()
    {
        byte[] key = RandomNumberGenerator.GetBytes(DeployMediaSecretEnvelopeProtector.KeySizeBytes);
        const string secret = "PfxPassword-DoNotLeak";
        SecretEnvelope envelope = Encrypt(Encoding.UTF8.GetBytes(secret), key) with
        {
            Ciphertext = "AAAA"
        };

        CryptographicException exception = Assert.Throws<CryptographicException>(
            () => DeployMediaSecretEnvelopeProtector.DecryptString(envelope, key));

        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(secret)), exception.ToString(), StringComparison.Ordinal);
    }

    private static SecretEnvelope Encrypt(byte[] plaintext, byte[] key)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] tag = new byte[16];
        byte[] ciphertext = new byte[plaintext.Length];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return new SecretEnvelope
        {
            Kind = DeployMediaSecretEnvelopeProtector.Kind,
            Algorithm = DeployMediaSecretEnvelopeProtector.Algorithm,
            KeyId = DeployMediaSecretEnvelopeProtector.KeyId,
            Nonce = Base64UrlEncode(nonce),
            Tag = Base64UrlEncode(tag),
            Ciphertext = Base64UrlEncode(ciphertext)
        };
    }

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
