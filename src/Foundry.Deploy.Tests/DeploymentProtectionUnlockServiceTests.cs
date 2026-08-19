// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Services.WinPe;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Security;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentProtectionUnlockServiceTests
{
    [Fact]
    public void TryUnlock_WithCorrectPassword_PopulatesSessionWithDeploymentKey()
    {
        using DeploymentMediaProtectionMaterial material = DeploymentMediaProtectionService.CreateProtected("correct horse battery staple");
        using var session = new DeploymentSecretKeySession();
        var service = new DeploymentProtectionUnlockService(session);

        bool unlocked = service.TryUnlock(Map(material.Settings), "correct horse battery staple");

        Assert.True(unlocked);
        Assert.True(session.IsUnlocked);
        byte[] key = session.GetKeyCopy();
        try
        {
            Assert.Equal(material.DeploymentKey, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    [Fact]
    public void TryUnlock_WithWrongPassword_DoesNotPopulateSession()
    {
        using DeploymentMediaProtectionMaterial material = DeploymentMediaProtectionService.CreateProtected("correct horse battery staple");
        using var session = new DeploymentSecretKeySession();
        var service = new DeploymentProtectionUnlockService(session);

        bool unlocked = service.TryUnlock(Map(material.Settings), "incorrect password");

        Assert.False(unlocked);
        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public void TryUnlock_WhenEnabledFlagIsClearedButMetadataRemains_StillValidatesPassword()
    {
        using DeploymentMediaProtectionMaterial material = DeploymentMediaProtectionService.CreateProtected("correct horse battery staple");
        DeployProtectionSettings settings = Map(material.Settings) with { IsEnabled = false };
        using var session = new DeploymentSecretKeySession();
        var service = new DeploymentProtectionUnlockService(session);

        bool unlocked = service.TryUnlock(settings, "correct horse battery staple");

        Assert.True(unlocked);
        Assert.True(session.IsUnlocked);
    }

    [Fact]
    public void TryUnlock_WithTamperedEnvelope_DoesNotExposeCryptographicFailure()
    {
        using DeploymentMediaProtectionMaterial material = DeploymentMediaProtectionService.CreateProtected("correct horse battery staple");
        DeployProtectionSettings settings = Map(material.Settings) with
        {
            ProtectedDeploymentKey = Map(material.Settings).ProtectedDeploymentKey with { Ciphertext = "AAAA" }
        };
        using var session = new DeploymentSecretKeySession();
        var service = new DeploymentProtectionUnlockService(session);

        bool unlocked = service.TryUnlock(settings, "correct horse battery staple");

        Assert.False(unlocked);
        Assert.False(session.IsUnlocked);
    }

    [Fact]
    public void Session_ClonesAndClearsOwnedKeyMaterial()
    {
        byte[] source = RandomNumberGenerator.GetBytes(32);
        byte[] expected = source.ToArray();
        using var session = new DeploymentSecretKeySession();

        session.SetKey(source);
        CryptographicOperations.ZeroMemory(source);
        byte[] copy = session.GetKeyCopy();

        Assert.Equal(expected, copy);
        session.Clear();
        Assert.False(session.IsUnlocked);
        Assert.Throws<InvalidOperationException>(() => session.GetKeyCopy());

        CryptographicOperations.ZeroMemory(copy);
        CryptographicOperations.ZeroMemory(expected);
    }

    private static DeployProtectionSettings Map(Foundry.Core.Models.Configuration.Deploy.DeployProtectionSettings settings)
    {
        Foundry.Core.Models.Configuration.SecretEnvelope sourceEnvelope =
            settings.ProtectedDeploymentKey ?? throw new InvalidOperationException("Protected deployment key is required.");

        return new DeployProtectionSettings
        {
            IsEnabled = settings.IsEnabled,
            KeyDerivationAlgorithm = settings.KeyDerivationAlgorithm ?? string.Empty,
            Iterations = settings.Iterations,
            Salt = settings.Salt ?? string.Empty,
            ProtectedDeploymentKey = new SecretEnvelope
            {
                Kind = sourceEnvelope.Kind,
                Algorithm = sourceEnvelope.Algorithm,
                KeyId = sourceEnvelope.KeyId,
                Nonce = sourceEnvelope.Nonce,
                Tag = sourceEnvelope.Tag,
                Ciphertext = sourceEnvelope.Ciphertext
            }
        };
    }
}
