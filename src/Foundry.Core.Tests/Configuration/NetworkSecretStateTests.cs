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
        state.Update(CreatePersonalWifiSettings(null) with
        {
            Wifi = CreatePersonalWifiSettings(null).Wifi with
            {
                Ssid = "UpdatedFoundry"
            }
        });

        Assert.Equal("ValidPassphrase123", state.PersonalWifiPassphrase);
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
