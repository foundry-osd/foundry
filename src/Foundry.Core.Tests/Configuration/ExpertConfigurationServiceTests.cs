using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Configuration;

public sealed class ExpertConfigurationServiceTests
{
    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsBusinessSettings()
    {
        var service = new ExpertConfigurationService();
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = Path.Combine(tempDirectory.Path, "config", "foundry.json");

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

        await service.SaveAsync(configurationPath, document);
        FoundryExpertConfigurationDocument loaded = await service.LoadAsync(configurationPath);

        Assert.True(File.Exists(configurationPath));
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
