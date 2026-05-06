using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class NetworkConfigurationValidatorTests
{
    [Fact]
    public void Validate_WhenPersonalWifiPassphraseIsTooShort_ReturnsPassphraseInvalid()
    {
        NetworkConfigurationValidationResult result = NetworkConfigurationValidator.Validate(
            new NetworkSettings
            {
                WifiProvisioned = true,
                Wifi = new WifiSettings
                {
                    IsEnabled = true,
                    Ssid = "CorpWiFi",
                    SecurityType = NetworkConfigurationValidator.WifiSecurityPersonal,
                    Passphrase = "short"
                }
            });

        Assert.Equal(NetworkConfigurationValidationCode.WifiPersonalPassphraseInvalid, result.Code);
    }

    [Fact]
    public void Validate_WhenWpa3EnterpriseTemplateAuthenticationDoesNotMatch_ReturnsMismatch()
    {
        using var tempDirectory = new TemporaryDirectory();
        string profilePath = Path.Combine(tempDirectory.Path, "wifi.xml");
        File.WriteAllText(
            profilePath,
            """
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
              <MSM>
                <security>
                  <authEncryption>
                    <authentication>WPA3ENT</authentication>
                  </authEncryption>
                </security>
              </MSM>
            </WLANProfile>
            """);

        NetworkConfigurationValidationResult result = NetworkConfigurationValidator.Validate(
            new NetworkSettings
            {
                WifiProvisioned = true,
                Wifi = new WifiSettings
                {
                    IsEnabled = true,
                    Ssid = "CorpWiFi",
                    SecurityType = NetworkConfigurationValidator.WifiSecurityEnterpriseWpa3192,
                    HasEnterpriseProfile = true,
                    EnterpriseProfileTemplatePath = profilePath
                }
            });

        Assert.Equal(NetworkConfigurationValidationCode.WifiEnterpriseAuthenticationMismatch, result.Code);
        Assert.Equal(NetworkConfigurationValidator.WifiSecurityEnterpriseWpa3, result.FormatArguments.Single());
    }

    [Fact]
    public void Validate_WhenWiredCertificateIsRequiredWithoutPath_ReturnsWiredCertificateRequired()
    {
        NetworkConfigurationValidationResult result = NetworkConfigurationValidator.Validate(
            new NetworkSettings
            {
                Dot1x = new Dot1xSettings
                {
                    IsEnabled = true,
                    ProfileTemplatePath = "wired.xml",
                    RequiresCertificate = true
                }
            });

        Assert.Equal(NetworkConfigurationValidationCode.WiredCertificateRequired, result.Code);
    }

    [Fact]
    public void SanitizeForPersistence_RemovesPlaintextWifiPassphrase()
    {
        NetworkSettings settings = new()
        {
            WifiProvisioned = true,
            Wifi = new WifiSettings
            {
                IsEnabled = true,
                Ssid = "CorpWiFi",
                SecurityType = NetworkConfigurationValidator.WifiSecurityPersonal,
                Passphrase = "supersecret"
            }
        };

        NetworkSettings sanitized = NetworkConfigurationValidator.SanitizeForPersistence(settings);

        Assert.Null(sanitized.Wifi.Passphrase);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Foundry.Core.Tests", Guid.NewGuid().ToString("N"));
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
