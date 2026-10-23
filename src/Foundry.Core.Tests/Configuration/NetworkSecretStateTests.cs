// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Configuration;

public sealed class NetworkSecretStateTests
{
    [Fact]
    public void Update_WhenPersonalWifiPassphraseIsMissingOnLaterSave_PreservesTransientSecret()
    {
        NetworkSecretState state = new();

        state.Update(CreatePersonalWifiSettings(), new NetworkSecrets { WifiPassphrase = "ValidPassphrase123" });
        state.Update(CreatePersonalWifiSettings());

        Assert.Equal("ValidPassphrase123", state.PersonalWifiPassphrase);
    }

    [Fact]
    public void Update_WhenSsidChanges_DoesNotReuseAnotherNetworksPassword()
    {
        NetworkSecretState state = new();
        state.Update(CreatePersonalWifiSettings(), new NetworkSecrets { WifiPassphrase = "ValidPassphrase123" });
        state.Update(CreatePersonalWifiSettings() with
        {
            Wifi = CreatePersonalWifiSettings().Wifi with
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
            Dot1x = new Dot1xSettings { IsEnabled = true, RequiresCertificate = true, Certificate = TestFileReferences.Detached("first.pfx") }
        };
        state.Update(settings, new NetworkSecrets { WiredCertificatePfxPassword = string.Empty });
        state.Update(settings);
        Assert.Equal(string.Empty, state.GetSecrets(settings).WiredCertificatePfxPassword);

        settings = settings with { Dot1x = settings.Dot1x with { Certificate = TestFileReferences.Detached("second.pfx") } };
        state.Update(settings);
        Assert.Null(state.GetSecrets(settings).WiredCertificatePfxPassword);
    }

    [Fact]
    public void GetSecrets_ReturnsOnlyPasswordsBoundToTheCurrentSettings()
    {
        NetworkSecretState state = new();
        AuthoringFileReference certificate = TestFileReferences.Detached("wifi.pfx");
        NetworkSettings settings = CreatePersonalWifiSettings() with
        {
            Dot1x = new Dot1xSettings { IsEnabled = true, RequiresCertificate = true, Certificate = certificate }
        };
        state.Update(settings, new NetworkSecrets { WifiPassphrase = " ValidPassphrase123 ", WiredCertificatePfxPassword = "pfx-password" });

        NetworkSecrets secrets = state.GetSecrets(settings);
        NetworkSecrets otherCertificate = state.GetSecrets(settings with
        {
            Dot1x = settings.Dot1x with { Certificate = TestFileReferences.Detached("other.pfx") }
        });

        Assert.Equal("ValidPassphrase123", secrets.WifiPassphrase);
        Assert.Equal("pfx-password", secrets.WiredCertificatePfxPassword);
        Assert.Null(otherCertificate.WiredCertificatePfxPassword);
    }

    [Fact]
    public void ClearPersonalWifiPassphrase_RemovesTransientSecret()
    {
        NetworkSecretState state = new();

        state.Update(CreatePersonalWifiSettings(), new NetworkSecrets { WifiPassphrase = "ValidPassphrase123" });
        state.ClearPersonalWifiPassphrase();

        Assert.Null(state.PersonalWifiPassphrase);
    }

    [Fact]
    public void Update_WhenPersonalWifiIsDisabled_ClearsTransientSecret()
    {
        NetworkSecretState state = new();

        state.Update(CreatePersonalWifiSettings(), new NetworkSecrets { WifiPassphrase = "ValidPassphrase123" });
        state.Update(new NetworkSettings());

        Assert.Null(state.PersonalWifiPassphrase);
    }

    private static NetworkSettings CreatePersonalWifiSettings()
    {
        return new NetworkSettings
        {
            WifiProvisioned = true,
            Wifi = new WifiSettings
            {
                IsEnabled = true,
                Ssid = "Foundry",
                SecurityType = NetworkConfigurationValidator.WifiSecurityPersonal
            }
        };
    }
}
