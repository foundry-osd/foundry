using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class ConnectConfigurationGeneratorTests
{
    [Fact]
    public void CreateProvisioningBundle_WhenNetworkIsDefault_WritesCompleteEffectiveConfiguration()
    {
        using var tempDirectory = new TemporaryDirectory();
        var generator = new ConnectConfigurationGenerator();

        FoundryConnectProvisioningBundle bundle = generator.CreateProvisioningBundle(
            new FoundryExpertConfigurationDocument(),
            tempDirectory.Path);

        using JsonDocument document = JsonDocument.Parse(bundle.ConfigurationJson);
        JsonElement root = document.RootElement;
        Assert.Equal(FoundryConnectConfigurationDocument.CurrentSchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.True(root.TryGetProperty("capabilities", out JsonElement capabilities));
        Assert.True(root.TryGetProperty("dot1x", out _));
        Assert.True(root.TryGetProperty("wifi", out _));
        Assert.True(root.TryGetProperty("internetProbe", out JsonElement internetProbe));
        Assert.False(capabilities.GetProperty("wifiProvisioned").GetBoolean());
        Assert.Equal(5, internetProbe.GetProperty("timeoutSeconds").GetInt32());
        Assert.NotEmpty(internetProbe.GetProperty("probeUris").EnumerateArray());
        Assert.Empty(bundle.AssetFiles);
    }

    [Fact]
    public void CreateProvisioningBundle_WhenProfilesAndCertificatesAreConfigured_UsesWpfCompatibleNetworkLayout()
    {
        using var tempDirectory = new TemporaryDirectory();
        string wiredProfilePath = tempDirectory.CreateFile("source", "wired.xml", "<LANProfile />");
        string wiredCertificatePath = tempDirectory.CreateFile("source", "wired.cer", "wired-cert");
        string wifiProfilePath = tempDirectory.CreateFile(
            "source",
            "wifi.xml",
            """
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
              <name>Corp WiFi</name>
              <MSM>
                <security>
                  <authEncryption>
                    <authentication>WPA3ENT</authentication>
                  </authEncryption>
                </security>
              </MSM>
            </WLANProfile>
            """);
        string wifiCertificatePath = tempDirectory.CreateFile("source", "wifi.cer", "wifi-cert");
        var generator = new ConnectConfigurationGenerator();

        FoundryConnectProvisioningBundle bundle = generator.CreateProvisioningBundle(
            new FoundryExpertConfigurationDocument
            {
                Network = new NetworkSettings
                {
                    WifiProvisioned = true,
                    Dot1x = new Dot1xSettings
                    {
                        IsEnabled = true,
                        ProfileTemplatePath = wiredProfilePath,
                        RequiresCertificate = true,
                        CertificatePath = wiredCertificatePath
                    },
                    Wifi = new WifiSettings
                    {
                        IsEnabled = true,
                        Ssid = "Corp WiFi",
                        SecurityType = NetworkConfigurationValidator.WifiSecurityEnterpriseWpa3,
                        HasEnterpriseProfile = true,
                        EnterpriseProfileTemplatePath = wifiProfilePath,
                        RequiresCertificate = true,
                        CertificatePath = wifiCertificatePath
                    }
                }
            },
            tempDirectory.Path);

        AssertAsset(bundle, tempDirectory.Path, @"Network\Wired\Profiles\wired.xml", @"Foundry\Config\Network\Wired\Profiles\wired.xml");
        AssertAsset(bundle, tempDirectory.Path, @"Network\Certificates\Wired\wired.cer", @"Foundry\Config\Network\Certificates\Wired\wired.cer");
        AssertAsset(bundle, tempDirectory.Path, @"Network\Wifi\Profiles\wifi.xml", @"Foundry\Config\Network\Wifi\Profiles\wifi.xml");
        AssertAsset(bundle, tempDirectory.Path, @"Network\Certificates\Wifi\wifi.cer", @"Foundry\Config\Network\Certificates\Wifi\wifi.cer");

        using JsonDocument document = JsonDocument.Parse(bundle.ConfigurationJson);
        JsonElement root = document.RootElement;
        Assert.Equal(@"Network\Wired\Profiles\wired.xml", root.GetProperty("dot1x").GetProperty("profileTemplatePath").GetString());
        Assert.Equal(@"Network\Certificates\Wired\wired.cer", root.GetProperty("dot1x").GetProperty("certificatePath").GetString());
        Assert.Equal(@"Network\Wifi\Profiles\wifi.xml", root.GetProperty("wifi").GetProperty("enterpriseProfileTemplatePath").GetString());
        Assert.Equal(@"Network\Certificates\Wifi\wifi.cer", root.GetProperty("wifi").GetProperty("certificatePath").GetString());
    }

    [Fact]
    public void CreateProvisioningBundle_WhenPersonalWifiHasTransientPassphrase_PreservesPassphraseForCurrentRuntime()
    {
        using var tempDirectory = new TemporaryDirectory();
        var generator = new ConnectConfigurationGenerator();

        FoundryConnectProvisioningBundle bundle = generator.CreateProvisioningBundle(
            new FoundryExpertConfigurationDocument
            {
                Network = new NetworkSettings
                {
                    WifiProvisioned = true,
                    Wifi = new WifiSettings
                    {
                        IsEnabled = true,
                        Ssid = "Corp WiFi",
                        SecurityType = NetworkConfigurationValidator.WifiSecurityPersonal,
                        Passphrase = "super-secret-passphrase"
                    }
                }
            },
            tempDirectory.Path);

        using JsonDocument document = JsonDocument.Parse(bundle.ConfigurationJson);
        Assert.Equal("super-secret-passphrase", document.RootElement.GetProperty("wifi").GetProperty("passphrase").GetString());
        Assert.Empty(bundle.AssetFiles);
    }

    [Fact]
    public void CreateProvisioningBundle_WhenCertificatePathsAreProvided_CopiesCertificatesEvenWhenNotRequired()
    {
        using var tempDirectory = new TemporaryDirectory();
        string wiredCertificatePath = tempDirectory.CreateFile("source", "wired.cer", "wired-cert");
        string wifiCertificatePath = tempDirectory.CreateFile("source", "wifi.cer", "wifi-cert");
        var generator = new ConnectConfigurationGenerator();

        FoundryConnectProvisioningBundle bundle = generator.CreateProvisioningBundle(
            new FoundryExpertConfigurationDocument
            {
                Network = new NetworkSettings
                {
                    WifiProvisioned = true,
                    Dot1x = new Dot1xSettings
                    {
                        IsEnabled = true,
                        ProfileTemplatePath = tempDirectory.CreateFile("source", "wired.xml", "<LANProfile />"),
                        RequiresCertificate = false,
                        CertificatePath = wiredCertificatePath
                    },
                    Wifi = new WifiSettings
                    {
                        IsEnabled = true,
                        Ssid = "Corp WiFi",
                        SecurityType = NetworkConfigurationValidator.WifiSecurityPersonal,
                        Passphrase = "super-secret-passphrase",
                        RequiresCertificate = false,
                        CertificatePath = wifiCertificatePath
                    }
                }
            },
            tempDirectory.Path);

        AssertAsset(bundle, tempDirectory.Path, @"Network\Certificates\Wired\wired.cer", @"Foundry\Config\Network\Certificates\Wired\wired.cer");
        AssertAsset(bundle, tempDirectory.Path, @"Network\Certificates\Wifi\wifi.cer", @"Foundry\Config\Network\Certificates\Wifi\wifi.cer");
        using JsonDocument document = JsonDocument.Parse(bundle.ConfigurationJson);
        Assert.Equal(@"Network\Certificates\Wired\wired.cer", document.RootElement.GetProperty("dot1x").GetProperty("certificatePath").GetString());
        Assert.Equal(@"Network\Certificates\Wifi\wifi.cer", document.RootElement.GetProperty("wifi").GetProperty("certificatePath").GetString());
    }

    private static void AssertAsset(
        FoundryConnectProvisioningBundle bundle,
        string stagingDirectoryPath,
        string stagedRelativePath,
        string relativeDestinationPath)
    {
        string expectedSourcePath = System.IO.Path.Combine(stagingDirectoryPath, "FoundryConnectAssets", stagedRelativePath);
        Assert.Contains(
            bundle.AssetFiles,
            asset => string.Equals(asset.SourcePath, expectedSourcePath, StringComparison.Ordinal) &&
                     string.Equals(asset.RelativeDestinationPath, relativeDestinationPath, StringComparison.Ordinal));
        Assert.True(File.Exists(expectedSourcePath));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Foundry.Core.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateFile(string directoryName, string fileName, string contents)
        {
            string directoryPath = System.IO.Path.Combine(Path, directoryName);
            Directory.CreateDirectory(directoryPath);
            string path = System.IO.Path.Combine(directoryPath, fileName);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
