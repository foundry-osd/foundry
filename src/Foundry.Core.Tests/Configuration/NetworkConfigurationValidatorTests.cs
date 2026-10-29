// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Tests.TestUtilities;

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
                    SecurityType = NetworkConfigurationValidator.WifiSecurityPersonal
                }
            },
            MediaInputs.None with { Network = new NetworkSecrets { WifiPassphrase = "short" } });

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
                    EnterpriseProfileTemplate = TestFileReferences.For(profilePath)
                }
            },
            TestFileReferences.Resolving(profilePath));

        Assert.Equal(NetworkConfigurationValidationCode.WifiEnterpriseAuthenticationMismatch, result.Code);
        Assert.Equal(NetworkConfigurationValidator.WifiSecurityEnterpriseWpa3, result.FormatArguments.Single());
    }

    [Fact]
    public void Validate_WhenWiredCertificateIsRequiredWithoutPath_ReturnsWiredCertificateRequired()
    {
        using var tempDirectory = new TemporaryDirectory();
        string profilePath = Path.Combine(tempDirectory.Path, "wired.xml");
        File.WriteAllText(profilePath, "<LANProfile />");

        NetworkConfigurationValidationResult result = NetworkConfigurationValidator.Validate(
            new NetworkSettings
            {
                Dot1x = new Dot1xSettings
                {
                    IsEnabled = true,
                    ProfileTemplate = TestFileReferences.For(profilePath),
                    RequiresCertificate = true
                }
            },
            TestFileReferences.Resolving(profilePath));

        Assert.Equal(NetworkConfigurationValidationCode.WiredCertificateRequired, result.Code);
    }

    [Fact]
    public void Validate_WhenWiredProfilePathDoesNotExist_ReturnsWiredProfileTemplateMissing()
    {
        NetworkConfigurationValidationResult result = NetworkConfigurationValidator.Validate(
            new NetworkSettings
            {
                Dot1x = new Dot1xSettings
                {
                    IsEnabled = true,
                    ProfileTemplate = TestFileReferences.Detached("missing.xml")
                }
            },
            MediaInputs.None);

        Assert.Equal(NetworkConfigurationValidationCode.WiredProfileTemplateMissing, result.Code);
    }

    [Fact]
    public void Validate_WhenWiredCertificatePathDoesNotExist_ReturnsWiredCertificateMissing()
    {
        using var tempDirectory = new TemporaryDirectory();
        string profilePath = Path.Combine(tempDirectory.Path, "wired.xml");
        File.WriteAllText(profilePath, "<LANProfile />");

        NetworkConfigurationValidationResult result = NetworkConfigurationValidator.Validate(
            new NetworkSettings
            {
                Dot1x = new Dot1xSettings
                {
                    IsEnabled = true,
                    ProfileTemplate = TestFileReferences.For(profilePath),
                    TrustedRootCertificates = [TestFileReferences.Detached("missing.cer")]
                }
            },
            TestFileReferences.Resolving(profilePath));

        Assert.Equal(NetworkConfigurationValidationCode.WiredCertificateMissing, result.Code);
    }

    [Fact]
    public void Validate_WhenWifiCertificatePathDoesNotExist_ReturnsWifiEnterpriseCertificateMissing()
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
                    <authentication>WPA2</authentication>
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
                    SecurityType = NetworkConfigurationValidator.WifiSecurityEnterprise,
                    HasEnterpriseProfile = true,
                    EnterpriseProfileTemplate = TestFileReferences.For(profilePath),
                    TrustedRootCertificates = [TestFileReferences.Detached("missing.cer")]
                }
            },
            TestFileReferences.Resolving(profilePath));

        Assert.Equal(NetworkConfigurationValidationCode.WifiEnterpriseCertificateMissing, result.Code);
    }

    [Theory]
    [InlineData("WPA2-Personal", NetworkConfigurationValidator.WifiSecurityLegacyWpa2Personal)]
    [InlineData("WPA3-SAE", NetworkConfigurationValidator.WifiSecurityWpa3Personal)]
    [InlineData("OWE", NetworkConfigurationValidator.WifiSecurityOwe)]
    [InlineData("Open", NetworkConfigurationValidator.WifiSecurityOpen)]
    [InlineData("WPA2-Enterprise", NetworkConfigurationValidator.WifiSecurityEnterprise)]
    [InlineData("unknown", NetworkConfigurationValidator.WifiSecurityEnterprise)]
    public void ResolveDiscoveredWifiSecurityType_MapsNativeAuthenticationToFoundrySecurityType(
        string authentication,
        string expected)
    {
        string result = NetworkConfigurationValidator.ResolveDiscoveredWifiSecurityType(authentication);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Validate_WiredTrustedRootOnly_SatisfiesRequiredCertificates()
    {
        using var directory = new TemporaryDirectory();
        string template = Path.Combine(directory.Path, "wired.xml"), root = Path.Combine(directory.Path, "root.cer");
        File.WriteAllText(template, "<LANProfile />");
        File.WriteAllBytes(root, [1, 2, 3]);

        NetworkConfigurationValidationResult result = NetworkConfigurationValidator.Validate(new NetworkSettings
        {
            Dot1x = new Dot1xSettings { IsEnabled = true, ProfileTemplate = TestFileReferences.For(template), RequiresCertificate = true, TrustedRootCertificates = [TestFileReferences.For(root)] }
        }, TestFileReferences.Resolving(template, root));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WiredMissingSecondRoot_ReturnsWiredCertificateMissing()
    {
        using var directory = new TemporaryDirectory();
        string template = Path.Combine(directory.Path, "wired.xml"), root = Path.Combine(directory.Path, "root.cer");
        File.WriteAllText(template, "<LANProfile />");
        File.WriteAllBytes(root, [1, 2, 3]);

        NetworkConfigurationValidationResult result = NetworkConfigurationValidator.Validate(new NetworkSettings
        {
            Dot1x = new Dot1xSettings
            {
                IsEnabled = true,
                ProfileTemplate = TestFileReferences.For(template),
                RequiresCertificate = true,
                TrustedRootCertificates = [TestFileReferences.For(root), TestFileReferences.Detached("gone.cer")]
            }
        }, TestFileReferences.Resolving(template, root));

        Assert.Equal(NetworkConfigurationValidationCode.WiredCertificateMissing, result.Code);
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
