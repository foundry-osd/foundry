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
            MediaInputs.None with { Network = new NetworkSecrets { WifiPassphrase = "short" } }, isMediaProtected: true);

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
            TestFileReferences.Resolving(profilePath),
            isMediaProtected: false);

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
            TestFileReferences.Resolving(profilePath),
            isMediaProtected: false);

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
            MediaInputs.None,
            isMediaProtected: false);

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
            TestFileReferences.Resolving(profilePath),
            isMediaProtected: false);

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
            TestFileReferences.Resolving(profilePath),
            isMediaProtected: false);

        Assert.Equal(NetworkConfigurationValidationCode.WifiEnterpriseCertificateMissing, result.Code);
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
        }, TestFileReferences.Resolving(template, root), isMediaProtected: false);

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
        }, TestFileReferences.Resolving(template, root), isMediaProtected: false);

        Assert.Equal(NetworkConfigurationValidationCode.WiredCertificateMissing, result.Code);
    }

    private static NetworkSettings PersonalWifi() => new()
    {
        WifiProvisioned = true,
        Wifi = new WifiSettings { IsEnabled = true, Ssid = "Lab", SecurityType = NetworkConfigurationValidator.WifiSecurityPersonal }
    };

    private static MediaInputs WithPassphrase(string passphrase = "lab-passphrase") =>
        MediaInputs.None with { Network = new NetworkSecrets { WifiPassphrase = passphrase } };

    [Theory]
    [InlineData(false, NetworkConfigurationValidationCode.WifiMediaProtectionRequired)]
    [InlineData(true, NetworkConfigurationValidationCode.None)]
    public void Validate_PersonalWifi_RequiresMediaProtection(bool isMediaProtected, NetworkConfigurationValidationCode expected) =>
        Assert.Equal(expected, NetworkConfigurationValidator.Validate(PersonalWifi(), WithPassphrase(), isMediaProtected).Code);

    [Fact]
    public void Validate_PersonalWifiWithInvalidPassphrase_ReportsThePassphraseFirst() =>
        Assert.Equal(
            NetworkConfigurationValidationCode.WifiPersonalPassphraseInvalid,
            NetworkConfigurationValidator.Validate(PersonalWifi(), WithPassphrase("short"), isMediaProtected: false).Code);

    [Fact]
    public void Validate_OpenWifi_NeedsNoProtection() =>
        Assert.True(NetworkConfigurationValidator.Validate(
            PersonalWifi() with { Wifi = PersonalWifi().Wifi with { SecurityType = NetworkConfigurationValidator.WifiSecurityOpen } },
            MediaInputs.None,
            isMediaProtected: false).IsValid);

    [Theory]
    [InlineData(true, NetworkConfigurationValidationCode.WiredMediaProtectionRequired)]
    [InlineData(false, NetworkConfigurationValidationCode.None)]
    public void Validate_WiredClientCertificate_RequiresMediaProtection(bool withClient, NetworkConfigurationValidationCode expected)
    {
        using var directory = new TemporaryDirectory();
        string template = Path.Combine(directory.Path, "wired.xml"), pfx = Path.Combine(directory.Path, "client.pfx"), root = Path.Combine(directory.Path, "root.cer");
        File.WriteAllText(template, "<LANProfile />");
        File.WriteAllBytes(pfx, [1]);
        File.WriteAllBytes(root, [2]);
        var settings = new NetworkSettings
        {
            Dot1x = new Dot1xSettings
            {
                IsEnabled = true,
                ProfileTemplate = TestFileReferences.For(template),
                RequiresCertificate = true,
                ClientCertificate = withClient ? TestFileReferences.For(pfx) : null,
                TrustedRootCertificates = [TestFileReferences.For(root)]
            }
        };

        Assert.Equal(expected, NetworkConfigurationValidator.Validate(settings, TestFileReferences.Resolving(template, pfx, root), isMediaProtected: false).Code);
    }

    [Theory]
    [InlineData(false, NetworkConfigurationValidationCode.WifiMediaProtectionRequired)]
    [InlineData(true, NetworkConfigurationValidationCode.None)]
    public void Validate_EnterpriseWifiClientCertificate_RequiresMediaProtection(bool isMediaProtected, NetworkConfigurationValidationCode expected)
    {
        using var directory = new TemporaryDirectory();
        string template = Path.Combine(directory.Path, "wifi.xml"), pfx = Path.Combine(directory.Path, "client.pfx");
        File.WriteAllText(template, "<WLANProfile />");
        File.WriteAllBytes(pfx, [1]);
        var settings = new NetworkSettings
        {
            WifiProvisioned = true,
            Wifi = new WifiSettings
            {
                IsEnabled = true,
                Ssid = "Lab",
                SecurityType = NetworkConfigurationValidator.WifiSecurityEnterprise,
                HasEnterpriseProfile = true,
                EnterpriseProfileTemplate = TestFileReferences.For(template),
                ClientCertificate = TestFileReferences.For(pfx)
            }
        };

        Assert.Equal(expected, NetworkConfigurationValidator.Validate(settings, TestFileReferences.Resolving(template, pfx), isMediaProtected).Code);
    }

    [Fact]
    public void RequiresMediaProtection_IgnoresDisabledFeaturesAndRoots()
    {
        NetworkSettings disabled = PersonalWifi() with { Wifi = PersonalWifi().Wifi with { IsEnabled = false } };

        Assert.True(NetworkConfigurationValidator.RequiresMediaProtection(PersonalWifi(), WithPassphrase()));
        Assert.False(NetworkConfigurationValidator.RequiresMediaProtection(disabled, WithPassphrase()));
        Assert.False(NetworkConfigurationValidator.RequiresMediaProtection(PersonalWifi(), MediaInputs.None));
        Assert.False(NetworkConfigurationValidator.RequiresMediaProtection(
            new NetworkSettings { Dot1x = new Dot1xSettings { IsEnabled = false, ClientCertificate = TestFileReferences.Detached("c.pfx") } },
            MediaInputs.None));
        Assert.False(NetworkConfigurationValidator.RequiresMediaProtection(
            new NetworkSettings { Dot1x = new Dot1xSettings { IsEnabled = true, TrustedRootCertificates = [TestFileReferences.Detached("r.cer")] } },
            MediaInputs.None));
        Assert.True(NetworkConfigurationValidator.RequiresMediaProtection(
            new NetworkSettings { Dot1x = new Dot1xSettings { IsEnabled = true, ClientCertificate = TestFileReferences.Detached("c.pfx") } },
            MediaInputs.None));
    }

    [Theory]
    [InlineData(NetworkConfigurationValidator.WifiSecurityPersonal, false, false, true, true)]
    [InlineData(NetworkConfigurationValidator.WifiSecurityOpen, false, false, true, false)]
    [InlineData(NetworkConfigurationValidator.WifiSecurityEnterprise, true, true, false, true)]
    [InlineData(NetworkConfigurationValidator.WifiSecurityEnterprise, true, false, false, false)]
    [InlineData(NetworkConfigurationValidator.WifiSecurityEnterprise, true, false, true, false)]
    [InlineData(NetworkConfigurationValidator.WifiSecurityEnterprise, false, true, false, true)]
    [InlineData(NetworkConfigurationValidator.WifiSecurityPersonal, true, false, true, true)]
    [InlineData(NetworkConfigurationValidator.WifiSecurityPersonal, false, true, false, false)]
    public void RequiresMediaProtection_FollowsTheWifiClassification(
        string securityType, bool hasEnterpriseProfile, bool withClient, bool withPassphrase, bool expected)
    {
        var settings = new NetworkSettings
        {
            WifiProvisioned = true,
            Wifi = new WifiSettings
            {
                IsEnabled = true,
                Ssid = "Lab",
                SecurityType = securityType,
                HasEnterpriseProfile = hasEnterpriseProfile,
                ClientCertificate = withClient ? TestFileReferences.Detached("client.pfx") : null,
                TrustedRootCertificates = [TestFileReferences.Detached("root.cer")]
            }
        };

        Assert.Equal(expected, NetworkConfigurationValidator.RequiresMediaProtection(settings, withPassphrase ? WithPassphrase() : MediaInputs.None));
        Assert.False(NetworkConfigurationValidator.RequiresMediaProtection(settings with { WifiProvisioned = false }, WithPassphrase()));
    }

    [Fact]
    public void RequiresMediaProtection_StaleEnterpriseFlagOnPersonalWifi_AgreesWithTheValidator()
    {
        // A personal profile whose enterprise flag was left set still provisions its passphrase, and the validator asks for protection.
        NetworkSettings settings = PersonalWifi() with { Wifi = PersonalWifi().Wifi with { HasEnterpriseProfile = true } };

        Assert.Equal(
            NetworkConfigurationValidationCode.WifiMediaProtectionRequired,
            NetworkConfigurationValidator.Validate(settings, WithPassphrase(), isMediaProtected: false).Code);
        Assert.True(NetworkConfigurationValidator.RequiresMediaProtection(settings, WithPassphrase()));
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
