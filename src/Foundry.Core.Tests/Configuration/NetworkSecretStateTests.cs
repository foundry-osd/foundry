// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class NetworkSecretStateTests
{
    [Fact]
    public void Update_WhenPersonalWifiPassphraseIsMissingOnLaterSave_PreservesTransientSecret()
    {
        NetworkSecretState state = new();

        state.Update(CreatePersonalWifiSettings("ValidPassphrase123"));
        state.Update(CreatePersonalWifiSettings(null));

        Assert.Equal("ValidPassphrase123", state.PersonalWifiPassphrase);
    }

    [Fact]
    public void Update_WhenSsidChanges_DoesNotReuseAnotherNetworksPassword()
    {
        NetworkSecretState state = new();
        state.Update(CreatePersonalWifiSettings("ValidPassphrase123"));
        state.Update(CreatePersonalWifiSettings(null) with
        {
            Wifi = CreatePersonalWifiSettings(null).Wifi with
            {
                Ssid = "UpdatedFoundry"
            }
        });

        Assert.Null(state.PersonalWifiPassphrase);
    }

    [Fact]
    public void CertificatePassword_PreservesBlankButClearsWhenCertificateChanges()
    {
        NetworkSecretState state = new();
        NetworkSettings settings = new()
        {
            Dot1x = new Dot1xSettings { IsEnabled = true, RequiresCertificate = true, CertificatePath = "first.pfx", CertificatePfxPassword = string.Empty }
        };
        state.Update(settings);
        settings = NetworkConfigurationValidator.SanitizeForPersistence(settings);
        state.Update(settings);
        Assert.Equal(string.Empty, state.ApplyRequiredSecrets(settings).Dot1x.CertificatePfxPassword);

        settings = settings with { Dot1x = settings.Dot1x with { CertificatePath = "second.pfx" } };
        state.Update(settings);
        Assert.Null(state.ApplyRequiredSecrets(settings).Dot1x.CertificatePfxPassword);
    }

    [Fact]
    public void ClearPersonalWifiPassphrase_RemovesTransientSecret()
    {
        NetworkSecretState state = new();

        state.Update(CreatePersonalWifiSettings("ValidPassphrase123"));
        state.ClearPersonalWifiPassphrase();

        Assert.Null(state.PersonalWifiPassphrase);
    }

    [Fact]
    public void Update_WhenPersonalWifiIsDisabled_ClearsTransientSecret()
    {
        NetworkSecretState state = new();

        state.Update(CreatePersonalWifiSettings("ValidPassphrase123"));
        state.Update(new NetworkSettings());

        Assert.Null(state.PersonalWifiPassphrase);
    }

    private static NetworkSettings CreatePersonalWifiSettings(string? passphrase)
    {
        return new NetworkSettings
        {
            WifiProvisioned = true,
            Wifi = new WifiSettings
            {
                IsEnabled = true,
                Ssid = "Foundry",
                SecurityType = NetworkConfigurationValidator.WifiSecurityPersonal,
                Passphrase = passphrase
            }
        };
    }
}
