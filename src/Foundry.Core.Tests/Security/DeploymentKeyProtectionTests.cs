// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Security;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.Security;

public sealed class DeploymentKeyProtectionTests
{
    private const string Password = "correct horse battery staple";

    [Fact]
    public void TryUnwrap_CorrectPassword_ReturnsTheDeploymentKey()
    {
        using DeploymentMediaProtectionMaterial material = DeploymentMediaProtectionService.CreateProtected(Password);

        Assert.Equal(material.DeploymentKey, DeploymentKeyProtection.TryUnwrap(material.Settings, Password));
    }

    [Fact]
    public void TryUnwrap_WrongPasswordOrForeignMetadata_ReturnsNull()
    {
        using DeploymentMediaProtectionMaterial material = DeploymentMediaProtectionService.CreateProtected(Password);

        Assert.Null(DeploymentKeyProtection.TryUnwrap(material.Settings, "wrong password value"));
        Assert.Null(DeploymentKeyProtection.TryUnwrap(material.Settings with { Iterations = 1 }, Password));
        Assert.Null(DeploymentKeyProtection.TryUnwrap(material.Settings with { Salt = "!!" }, Password));
        Assert.Null(DeploymentKeyProtection.TryUnwrap(material.Settings with { ProtectedDeploymentKey = null }, Password));
        Assert.Null(DeploymentKeyProtection.TryUnwrap(new Foundry.Core.Models.Configuration.Deploy.DeployProtectionSettings(), Password));
    }

    [Fact]
    public void KeyCheck_AcceptsOnlyTheDeploymentKey()
    {
        using DeploymentMediaProtectionMaterial material = DeploymentMediaProtectionService.CreateProtected(Password);
        SecretEnvelope check = material.Settings.KeyCheck!;

        Assert.Equal(DeploymentKeyProtection.KeyCheckKeyId, check.KeyId);
        Assert.True(DeploymentKeyProtection.VerifyKeyCheck(check, material.DeploymentKey));
        Assert.False(DeploymentKeyProtection.VerifyKeyCheck(check, new byte[32]));
        Assert.False(DeploymentKeyProtection.VerifyKeyCheck(check, new byte[16]));
        Assert.False(DeploymentKeyProtection.VerifyKeyCheck(check with { Tag = "AAAAAAAAAAAAAAAAAAAAAA" }, material.DeploymentKey));
        Assert.False(DeploymentKeyProtection.VerifyKeyCheck(null, material.DeploymentKey));
    }

    [Fact]
    public void KeyCheck_RejectsAnotherLabelUnderTheDeploymentKey()
    {
        using DeploymentMediaProtectionMaterial material = DeploymentMediaProtectionService.CreateProtected(Password);
        SecretEnvelope otherLabel = MediaSecretEnvelopeProtector.EncryptBytes(
            "foundry-deployment-key-check-v2"u8.ToArray(), material.DeploymentKey, DeploymentKeyProtection.KeyCheckKeyId);

        Assert.False(DeploymentKeyProtection.VerifyKeyCheck(otherLabel, material.DeploymentKey));
    }

    [Fact]
    public void KeyCheck_RejectsTheLabelUnderAForeignKeyId()
    {
        using DeploymentMediaProtectionMaterial material = DeploymentMediaProtectionService.CreateProtected(Password);
        SecretEnvelope foreignKeyId = MediaSecretEnvelopeProtector.EncryptBytes(
            "foundry-deployment-key-check-v1"u8.ToArray(), material.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId);

        Assert.False(DeploymentKeyProtection.VerifyKeyCheck(foreignKeyId, material.DeploymentKey));
        Assert.False(DeploymentKeyProtection.VerifyKeyCheck(
            material.Settings.KeyCheck! with { KeyId = MediaSecretEnvelopeProtector.DeploymentKeyId }, material.DeploymentKey));
    }

    [Fact]
    public void KeyCheck_IsNotAMediaKeyedSecret()
    {
        using DeploymentMediaProtectionMaterial material = DeploymentMediaProtectionService.CreateProtected(Password);

        Assert.False(MediaSecretEnvelopeProtector.HasEncryptedSecrets(JsonSerializer.Serialize(material.Settings, ConfigurationJsonDefaults.SerializerOptions)));
    }

    [Fact]
    public void CreateUnprotected_HasNoKeyCheck()
    {
        using DeploymentMediaProtectionMaterial material = DeploymentMediaProtectionService.CreateUnprotected();

        Assert.Null(material.Settings.KeyCheck);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    public void RetryDelay_IsProgressiveAndCapped(int failedAttempt, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), DeploymentPasswordRetryPolicy.GetDelay(failedAttempt));

    [Fact]
    public void RetryDelay_RejectsNonPositiveAttempt() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DeploymentPasswordRetryPolicy.GetDelay(0));
}
