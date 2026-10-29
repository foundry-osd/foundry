// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Configuration;

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
}
