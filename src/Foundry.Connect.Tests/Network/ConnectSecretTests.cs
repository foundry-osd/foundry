// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Connect.Configuration;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Security;

namespace Foundry.Connect.Tests.Network;

public sealed class ConnectSecretTests
{
    [Fact]
    public void Available_ExposesValueUntilCleared()
    {
        char[] buffer = "pfx-password".ToCharArray();
        ConnectSecret secret = ConnectSecret.Available(buffer);

        Assert.Equal("pfx-password", new string(secret.Value));
        secret.Clear();

        Assert.Equal(ConnectSecretStatus.Cleared, secret.Status);
        Assert.All(buffer, static character => Assert.Equal('\0', character));
        Assert.Throws<InvalidOperationException>(() => { _ = secret.Value; });
        secret.Clear();
    }

    [Fact]
    public void Unavailable_CarriesFailureAndNoValue()
    {
        ConnectSecret secret = ConnectSecret.Unavailable(ConnectSecretFailure.MediaKeyMissing);

        Assert.Equal(ConnectSecretStatus.Unavailable, secret.Status);
        Assert.Equal(ConnectSecretFailure.MediaKeyMissing, secret.Failure);
        Assert.Throws<InvalidOperationException>(() => { _ = secret.Value; });
    }

    [Fact]
    public void ToString_NeverShowsTheValue()
    {
        var secrets = new ConnectRuntimeSecrets(
            ConnectSecret.Available("wifi-passphrase".ToCharArray()),
            ConnectSecret.NotProvisioned(),
            ConnectSecret.Available("wired-password".ToCharArray()));

        Assert.DoesNotContain("wifi-passphrase", secrets.WifiPassphrase.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("wired-password", secrets.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ClearAll_ClearsEverySecret()
    {
        var secrets = new ConnectRuntimeSecrets(
            ConnectSecret.Available("a".ToCharArray()),
            ConnectSecret.Available("b".ToCharArray()),
            ConnectSecret.Unavailable(ConnectSecretFailure.DecryptionFailed));

        secrets.ClearAll();

        Assert.Equal(ConnectSecretStatus.Cleared, secrets.WifiPassphrase.Status);
        Assert.Equal(ConnectSecretStatus.Cleared, secrets.WifiCertificatePfxPassword.Status);
        Assert.Equal(ConnectSecretStatus.Unavailable, secrets.WiredCertificatePfxPassword.Status);
    }

    [Fact]
    public void Locked_UnlocksOnlyWithTheDeploymentKey()
    {
        byte[] key = MediaSecretEnvelopeProtector.GenerateMediaKey();
        SecretEnvelope envelope = MediaSecretEnvelopeProtector.EncryptString("lab-passphrase", key, MediaSecretEnvelopeProtector.DeploymentKeyId);
        ConnectSecret wrong = ConnectSecret.Locked(envelope), right = ConnectSecret.Locked(envelope);

        Assert.Equal(ConnectSecretStatus.Locked, right.Status);
        Assert.Throws<InvalidOperationException>(() => { _ = right.Value; });
        Assert.False(wrong.TryUnlock(new byte[32]));
        Assert.Equal(ConnectSecretFailure.DecryptionFailed, wrong.Failure);
        Assert.True(right.TryUnlock(key));
        Assert.Equal("lab-passphrase", new string(right.Value));
    }

    [Fact]
    public void Clear_KeepsALockedSecretUnlockable()
    {
        byte[] key = MediaSecretEnvelopeProtector.GenerateMediaKey();
        ConnectSecret secret = ConnectSecret.Locked(MediaSecretEnvelopeProtector.EncryptString("w", key, MediaSecretEnvelopeProtector.DeploymentKeyId));

        secret.Clear();

        Assert.Equal(ConnectSecretStatus.Locked, secret.Status);
        Assert.True(secret.TryUnlock(key));
        Assert.False(secret.TryUnlock(key));
    }

    [Fact]
    public void ProtectedContent_UnlocksClearsAndNeverPrintsBytes()
    {
        byte[] key = MediaSecretEnvelopeProtector.GenerateMediaKey();
        var content = ConnectProtectedContent.Locked(MediaSecretEnvelopeProtector.EncryptBytes([1, 2, 3], key, MediaSecretEnvelopeProtector.DeploymentKeyId));

        Assert.True(content.TryUnlock(key));
        Assert.Equal([1, 2, 3], content.Content.ToArray());
        Assert.Equal("ConnectProtectedContent(Available)", content.ToString());
        content.Clear();
        Assert.Equal(ConnectSecretStatus.Cleared, content.Status);
    }

    [Fact]
    public void RuntimeSecrets_UnlockEverythingOnce()
    {
        byte[] key = MediaSecretEnvelopeProtector.GenerateMediaKey();
        SecretEnvelope Encrypt(string value) => MediaSecretEnvelopeProtector.EncryptString(value, key, MediaSecretEnvelopeProtector.DeploymentKeyId);
        var secrets = new ConnectRuntimeSecrets(ConnectSecret.Locked(Encrypt("p")), ConnectSecret.NotProvisioned(), ConnectSecret.Locked(Encrypt("w")),
            wiredClientCertificate: ConnectProtectedContent.Locked(MediaSecretEnvelopeProtector.EncryptBytes([9], key, MediaSecretEnvelopeProtector.DeploymentKeyId)));

        Assert.True(secrets.IsWiredLocked);
        Assert.True(secrets.IsWifiLocked);
        Assert.Equal(3, secrets.Unlock(key));
        Assert.False(secrets.IsWiredLocked);
        Assert.False(secrets.IsWifiLocked);
        Assert.Equal(0, secrets.Unlock(key));
    }

    [Fact]
    public void ProtectedContent_WrongKey_IsUnavailableWithTheFailureType()
    {
        byte[] key = MediaSecretEnvelopeProtector.GenerateMediaKey();
        var content = ConnectProtectedContent.Locked(MediaSecretEnvelopeProtector.EncryptBytes([1, 2, 3], key, MediaSecretEnvelopeProtector.DeploymentKeyId));

        Assert.False(content.TryUnlock(new byte[32]));
        Assert.Equal(ConnectSecretStatus.Unavailable, content.Status);
        Assert.Equal(ConnectSecretFailure.DecryptionFailed, content.Failure);
        Assert.Equal(nameof(CryptographicException), content.FailureType);
        Assert.Throws<InvalidOperationException>(() => { _ = content.Content; });
    }

    [Fact]
    public void RuntimeSecrets_UnlockWithAWrongLengthKey_ThrowsAndChangesNothing()
    {
        byte[] key = MediaSecretEnvelopeProtector.GenerateMediaKey();
        ConnectRuntimeSecrets secrets = LockedSecrets(key, protection: null);

        Assert.Throws<ArgumentException>(() => secrets.Unlock(new byte[16]));
        Assert.Equal(ConnectSecretStatus.Locked, secrets.WifiPassphrase.Status);
        Assert.Equal(ConnectSecretStatus.Locked, secrets.WiredClientCertificate.Status);
    }

    [Fact]
    public void RuntimeSecrets_UnlockWithAWrongKeyAndNoKeyCheck_MakesTheLockedItemsUnavailable()
    {
        ConnectRuntimeSecrets secrets = LockedSecrets(MediaSecretEnvelopeProtector.GenerateMediaKey(), protection: null);

        Assert.Equal(0, secrets.Unlock(MediaSecretEnvelopeProtector.GenerateMediaKey()));
        Assert.Equal(ConnectSecretFailure.DecryptionFailed, secrets.WifiPassphrase.Failure);
        Assert.Equal(ConnectSecretFailure.DecryptionFailed, secrets.WiredClientCertificate.Failure);
        Assert.False(secrets.IsWifiLocked);
        Assert.False(secrets.IsWiredLocked);
    }

    [Fact]
    public void RuntimeSecrets_UnlockWithAKeyThatFailsTheKeyCheck_ThrowsAndChangesNothing()
    {
        byte[] key = MediaSecretEnvelopeProtector.GenerateMediaKey();
        ConnectRuntimeSecrets secrets = LockedSecrets(key, new DeployProtectionSettings { IsEnabled = true, KeyCheck = DeploymentKeyProtection.CreateKeyCheck(key) });

        Assert.Throws<ArgumentException>(() => secrets.Unlock(MediaSecretEnvelopeProtector.GenerateMediaKey()));
        Assert.True(secrets.IsWifiLocked);
        Assert.True(secrets.IsWiredLocked);
        Assert.Equal(2, secrets.Unlock(key));
    }

    private static ConnectRuntimeSecrets LockedSecrets(byte[] key, DeployProtectionSettings? protection) => new(
        ConnectSecret.Locked(MediaSecretEnvelopeProtector.EncryptString("p", key, MediaSecretEnvelopeProtector.DeploymentKeyId)),
        ConnectSecret.NotProvisioned(),
        ConnectSecret.NotProvisioned(),
        wiredClientCertificate: ConnectProtectedContent.Locked(MediaSecretEnvelopeProtector.EncryptBytes([9], key, MediaSecretEnvelopeProtector.DeploymentKeyId)),
        protection: protection);
}
