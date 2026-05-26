using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;

namespace Foundry.Core.Services.Configuration;

internal static class ConnectSecretEnvelopeProtector
{
    public const string Kind = MediaSecretEnvelopeProtector.Kind;
    public const string Algorithm = MediaSecretEnvelopeProtector.Algorithm;
    public const string KeyId = MediaSecretEnvelopeProtector.KeyId;
    public const int KeySizeBytes = MediaSecretEnvelopeProtector.KeySizeBytes;
    public const int NonceSizeBytes = MediaSecretEnvelopeProtector.NonceSizeBytes;
    public const int TagSizeBytes = MediaSecretEnvelopeProtector.TagSizeBytes;

    public static byte[] GenerateMediaKey()
    {
        return MediaSecretEnvelopeProtector.GenerateMediaKey();
    }

    public static SecretEnvelope Encrypt(string plaintext, byte[] key)
    {
        return MediaSecretEnvelopeProtector.EncryptString(plaintext, key);
    }
}
