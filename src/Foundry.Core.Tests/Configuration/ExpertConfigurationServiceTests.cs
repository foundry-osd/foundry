using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class ExpertConfigurationServiceTests
{
    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsBusinessSettings()
    {
        var service = new ExpertConfigurationService();

        var document = new FoundryExpertConfigurationDocument
        {
            Network = new NetworkSettings
            {
                WifiProvisioned = true,
                Wifi = new WifiSettings
                {
                    IsEnabled = true,
                    Ssid = "CorpWiFi",
                    SecurityType = "WPA2/WPA3-Personal",
                    Passphrase = "supersecret"
                }
            },
            Customization = new CustomizationSettings
            {
                MachineNaming = new MachineNamingSettings
                {
                    IsEnabled = true,
                    Prefix = "FD-",
                    AutoGenerateName = true,
                    AllowManualSuffixEdit = false
                }
            }
        };

        string json = service.Serialize(document);
        FoundryExpertConfigurationDocument loaded = service.Deserialize(json);

        Assert.True(loaded.Network.WifiProvisioned);
        Assert.Equal("CorpWiFi", loaded.Network.Wifi.Ssid);
        Assert.Equal("FD-", loaded.Customization.MachineNaming.Prefix);
        Assert.True(loaded.Customization.MachineNaming.AutoGenerateName);
        Assert.False(loaded.Customization.MachineNaming.AllowManualSuffixEdit);
    }

    [Fact]
    public void Deserialize_WhenJsonIsNullLiteral_ReturnsDefaultDocument()
    {
        var service = new ExpertConfigurationService();

        FoundryExpertConfigurationDocument document = service.Deserialize("null");

        Assert.Equal(FoundryExpertConfigurationDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.False(document.Network.WifiProvisioned);
        Assert.False(document.Autopilot.IsEnabled);
    }
}
