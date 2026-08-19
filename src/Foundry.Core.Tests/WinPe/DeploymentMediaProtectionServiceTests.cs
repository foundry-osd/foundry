// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.WinPe;
using Foundry.Utilities.Security;

namespace Foundry.Core.Tests.WinPe;

public sealed class DeploymentMediaProtectionServiceTests
{
    [Fact]
    public void CreateProtected_WrapsDeploymentKeyWithoutPersistingPassword()
    {
        using DeploymentMediaProtectionMaterial material =
            DeploymentMediaProtectionService.CreateProtected("deployment passphrase".AsSpan());

        Assert.True(material.Settings.IsEnabled);
        Assert.Equal("pbkdf2-sha256", material.Settings.KeyDerivationAlgorithm);
        Assert.Equal(600_000, material.Settings.Iterations);
        Assert.Equal(32, material.DeploymentKey.Length);
        Assert.NotNull(material.Settings.ProtectedDeploymentKey);
        Assert.DoesNotContain(
            "deployment passphrase",
            JsonSerializer.Serialize(material.Settings),
            StringComparison.Ordinal);
    }

    [Fact]
    public void CreateProtected_EnvelopeDecryptsToGeneratedDeploymentKey()
    {
        const string password = "deployment passphrase";
        using DeploymentMediaProtectionMaterial material =
            DeploymentMediaProtectionService.CreateProtected(password.AsSpan());
        byte[] salt = Base64Url.Decode(material.Settings.Salt!);
        byte[] derivedKey = PasswordKeyDerivation.DeriveKey(
            password.AsSpan(),
            salt,
            material.Settings.Iterations,
            AesGcmEncryption.KeySizeBytes);

        try
        {
            SecretEnvelope envelope = material.Settings.ProtectedDeploymentKey!;
            byte[] decrypted = AesGcmEncryption.Decrypt(
                new AesGcmPayload(
                    Base64Url.Decode(envelope.Nonce),
                    Base64Url.Decode(envelope.Tag),
                    Base64Url.Decode(envelope.Ciphertext)),
                derivedKey);

            try
            {
                Assert.Equal(material.DeploymentKey, decrypted);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(decrypted);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derivedKey);
        }
    }

    [Fact]
    public void CreateUnprotected_GeneratesDeploymentKeyWithoutProtectionMetadata()
    {
        using DeploymentMediaProtectionMaterial material = DeploymentMediaProtectionService.CreateUnprotected();

        Assert.Equal(32, material.DeploymentKey.Length);
        Assert.False(material.Settings.IsEnabled);
        Assert.Null(material.Settings.ProtectedDeploymentKey);
    }

    [Fact]
    public void Dispose_ClearsOwnedDeploymentKey()
    {
        DeploymentMediaProtectionMaterial material = DeploymentMediaProtectionService.CreateUnprotected();
        byte[] ownedKey = material.DeploymentKey;

        material.Dispose();

        Assert.All(ownedKey, value => Assert.Equal(0, value));
    }
}
