using System.Globalization;
using System.Resources;
using System.Text.Json;
using Foundry.Models.Configuration;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;

namespace Foundry.Tests;

public sealed class FoundryConnectProvisioningServiceTests
{
    [Fact]
    public void Prepare_WhenWifiIsEnabledWithoutProvisioning_ThrowsInvalidOperationException()
    {
        var service = new FoundryConnectProvisioningService(new TestLocalizationService());
        var document = new FoundryExpertConfigurationDocument
        {
            Network = new NetworkSettings
            {
                WifiProvisioned = false,
                Wifi = new WifiSettings
                {
                    IsEnabled = true,
                    Ssid = "CorpWiFi",
                    SecurityType = "WPA2/WPA3-Personal",
                    Passphrase = "supersecret"
                }
            }
        };

        using var tempDirectory = new TemporaryDirectory();

        Assert.Throws<InvalidOperationException>(() => service.Prepare(document, tempDirectory.Path));
    }

    [Fact]
    public void Prepare_WhenEnterpriseWifiIsProvisioned_CopiesAssetsAndHardensConfiguration()
    {
        var service = new FoundryConnectProvisioningService(new TestLocalizationService());
        using var tempDirectory = new TemporaryDirectory();
        string wifiProfilePath = CreateWifiProfile(tempDirectory.Path, "WPA3ENT");
        string certificatePath = CreateFile(tempDirectory.Path, "wifi.cer", "certificate");

        var document = new FoundryExpertConfigurationDocument
        {
            Network = new NetworkSettings
            {
                WifiProvisioned = true,
                Wifi = new WifiSettings
                {
                    IsEnabled = true,
                    Ssid = "CorpWiFi",
                    SecurityType = "WPA3ENT",
                    Passphrase = "should-be-removed",
                    HasEnterpriseProfile = true,
                    EnterpriseProfileTemplatePath = wifiProfilePath,
                    RequiresCertificate = true,
                    CertificatePath = certificatePath,
                    AllowRuntimeCredentials = true
                }
            }
        };

        FoundryConnectProvisioningBundle bundle = service.Prepare(document, tempDirectory.Path);
        FoundryConnectConfigurationDocument? configuration = JsonSerializer.Deserialize<FoundryConnectConfigurationDocument>(
            bundle.ConfigurationJson,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        Assert.NotNull(configuration);
        Assert.True(configuration.Capabilities.WifiProvisioned);
        Assert.True(configuration.Wifi.IsEnabled);
        Assert.Equal("Network\\Wifi\\Profiles\\wifi-enterprise.xml", configuration.Wifi.EnterpriseProfileTemplatePath);
        Assert.Equal("Network\\Certificates\\Wifi\\wifi.cer", configuration.Wifi.CertificatePath);
        Assert.Null(configuration.Wifi.Passphrase);
        Assert.False(configuration.Wifi.AllowRuntimeCredentials);
        Assert.Equal(NetworkAuthenticationMode.UserOnly, configuration.Wifi.EnterpriseAuthenticationMode);
        Assert.Equal(2, bundle.AssetFiles.Count);
        Assert.Contains(bundle.AssetFiles, asset => asset.RelativeDestinationPath == "Foundry\\Config\\Network\\Wifi\\Profiles\\wifi-enterprise.xml");
        Assert.Contains(bundle.AssetFiles, asset => asset.RelativeDestinationPath == "Foundry\\Config\\Network\\Certificates\\Wifi\\wifi.cer");
    }

    private static string CreateWifiProfile(string directoryPath, string authentication)
    {
        const string profileTemplate = """
<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
  <name>Corp WiFi</name>
  <MSM>
    <security>
      <authEncryption>
        <authentication>{0}</authentication>
      </authEncryption>
    </security>
  </MSM>
</WLANProfile>
""";

        return CreateFile(directoryPath, "wifi-enterprise.xml", string.Format(CultureInfo.InvariantCulture, profileTemplate, authentication));
    }

    private static string CreateFile(string directoryPath, string fileName, string contents)
    {
        string filePath = System.IO.Path.Combine(directoryPath, fileName);
        File.WriteAllText(filePath, contents);
        return filePath;
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        public CultureInfo CurrentCulture { get; private set; } = CultureInfo.InvariantCulture;

        public StringsWrapper Strings { get; } = new(
            new ResourceManager("Foundry.Resources.AppStrings", typeof(FoundryConnectProvisioningService).Assembly),
            CultureInfo.InvariantCulture);

        public event EventHandler? LanguageChanged;

        public void SetCulture(CultureInfo culture)
        {
            CurrentCulture = culture;
            Strings.SetCulture(culture);
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Foundry.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
