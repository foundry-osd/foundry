// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Configuration;

public sealed class ConnectConfigurationGeneratorTests
{
    private const string FixturePassword = "correct horse battery staple";

    [Fact]
    public void CreateProvisioningBundle_WhenNetworkIsDefault_WritesCompleteEffectiveConfiguration()
    {
        using var tempDirectory = new TemporaryDirectory();
        var generator = new ConnectConfigurationGenerator();

        FoundryConnectProvisioningBundle bundle = generator.CreateProvisioningBundle(
            new FoundryConfigurationDocument(),
            MediaInputs.None,
            tempDirectory.Path);

        using JsonDocument document = JsonDocument.Parse(ConnectJson(bundle));
        JsonElement root = document.RootElement;
        Assert.Equal(new[] { "capabilities", "dot1x", "wifi" }, root.EnumerateObject().Select(property => property.Name));
        Assert.False(root.GetProperty("capabilities").GetProperty("wifiProvisioned").GetBoolean());
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
            new FoundryConfigurationDocument
            {
                Network = new NetworkSettings
                {
                    WifiProvisioned = true,
                    Dot1x = new Dot1xSettings
                    {
                        IsEnabled = true,
                        ProfileTemplate = TestFileReferences.For(wiredProfilePath),
                        RequiresCertificate = true,
                        TrustedRootCertificates = [TestFileReferences.For(wiredCertificatePath)]
                    },
                    Wifi = new WifiSettings
                    {
                        IsEnabled = true,
                        Ssid = "Corp WiFi",
                        SecurityType = NetworkConfigurationValidator.WifiSecurityEnterpriseWpa3,
                        HasEnterpriseProfile = true,
                        EnterpriseProfileTemplate = TestFileReferences.For(wifiProfilePath),
                        RequiresCertificate = true,
                        TrustedRootCertificates = [TestFileReferences.For(wifiCertificatePath)]
                    }
                }
            },
            SourceInputs(tempDirectory),
            tempDirectory.Path);

        AssertAsset(bundle, tempDirectory.Path, @"Network\Wired\Profiles\wired.xml", @"Foundry\Config\Network\Wired\Profiles\wired.xml");
        AssertAsset(bundle, tempDirectory.Path, @"Network\Certificates\Wired\wired.cer", @"Foundry\Config\Network\Certificates\Wired\wired.cer");
        AssertAsset(bundle, tempDirectory.Path, @"Network\Wifi\Profiles\wifi.xml", @"Foundry\Config\Network\Wifi\Profiles\wifi.xml");
        AssertAsset(bundle, tempDirectory.Path, @"Network\Certificates\Wifi\wifi.cer", @"Foundry\Config\Network\Certificates\Wifi\wifi.cer");

        using JsonDocument document = JsonDocument.Parse(ConnectJson(bundle));
        JsonElement dot1x = document.RootElement.GetProperty("dot1x");
        JsonElement wifi = document.RootElement.GetProperty("wifi");
        Assert.Equal(@"Network\Wired\Profiles\wired.xml", dot1x.GetProperty("profileTemplatePath").GetString());
        Assert.Equal(@"Network\Certificates\Wired\wired.cer", Assert.Single(dot1x.GetProperty("trustedRootCertificates").EnumerateArray()).GetString());
        Assert.Equal(@"Network\Wifi\Profiles\wifi.xml", wifi.GetProperty("enterpriseProfileTemplatePath").GetString());
        Assert.Equal(@"Network\Certificates\Wifi\wifi.cer", Assert.Single(wifi.GetProperty("trustedRootCertificates").EnumerateArray()).GetString());
        Assert.False(dot1x.TryGetProperty("certificatePath", out _));
        Assert.False(wifi.TryGetProperty("certificatePath", out _));
        Assert.False(wifi.TryGetProperty("isEnterpriseProfileTemplateProtected", out _));
    }

    [Fact]
    public void CreateProvisioningBundle_WhenPersonalWifiHasTransientPassphrase_WritesDeploymentKeyedSecretEnvelope()
    {
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial protection = Protected();
        var generator = new ConnectConfigurationGenerator();

        FoundryConnectProvisioningBundle bundle = generator.CreateProvisioningBundle(
            new FoundryConfigurationDocument
            {
                Network = new NetworkSettings
                {
                    WifiProvisioned = true,
                    Wifi = new WifiSettings
                    {
                        IsEnabled = true,
                        Ssid = "Corp WiFi",
                        SecurityType = NetworkConfigurationValidator.WifiSecurityPersonal
                    }
                }
            },
            SourceInputs(tempDirectory, new NetworkSecrets { WifiPassphrase = "super-secret-passphrase" }),
            tempDirectory.Path,
            protection);

        using JsonDocument document = JsonDocument.Parse(ConnectJson(bundle));
        JsonElement wifi = document.RootElement.GetProperty("wifi");
        Assert.False(wifi.TryGetProperty("passphrase", out _));
        Assert.True(wifi.TryGetProperty("passphraseSecret", out JsonElement envelope));
        Assert.Equal("encrypted", envelope.GetProperty("kind").GetString());
        Assert.Equal("aes-gcm-v1", envelope.GetProperty("algorithm").GetString());
        Assert.Equal("deployment", envelope.GetProperty("keyId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(envelope.GetProperty("nonce").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(envelope.GetProperty("tag").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(envelope.GetProperty("ciphertext").GetString()));
        Assert.DoesNotContain("super-secret-passphrase", ConnectJson(bundle), StringComparison.Ordinal);
        Assert.Equal(
            "super-secret-passphrase",
            MediaSecretEnvelopeProtector.DecryptString(bundle.Configuration.Wifi.PassphraseSecret!, protection.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId));
        Assert.Empty(bundle.AssetFiles);
    }

    [Fact]
    public void CreateProvisioningBundle_WhenCertificatePathsAreProvided_CopiesCertificatesEvenWhenNotRequired()
    {
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial protection = Protected();
        string wiredCertificatePath = tempDirectory.CreateFile("source", "wired.cer", "wired-cert");
        string wifiCertificatePath = tempDirectory.CreateFile("source", "wifi.cer", "wifi-cert");
        var generator = new ConnectConfigurationGenerator();

        FoundryConnectProvisioningBundle bundle = generator.CreateProvisioningBundle(
            new FoundryConfigurationDocument
            {
                Network = new NetworkSettings
                {
                    WifiProvisioned = true,
                    Dot1x = new Dot1xSettings
                    {
                        IsEnabled = true,
                        ProfileTemplate = TestFileReferences.For(tempDirectory.CreateFile("source", "wired.xml", "<LANProfile />")),
                        RequiresCertificate = false,
                        TrustedRootCertificates = [TestFileReferences.For(wiredCertificatePath)]
                    },
                    Wifi = new WifiSettings
                    {
                        IsEnabled = true,
                        Ssid = "Corp WiFi",
                        SecurityType = NetworkConfigurationValidator.WifiSecurityPersonal,
                        RequiresCertificate = false,
                        TrustedRootCertificates = [TestFileReferences.For(wifiCertificatePath)]
                    }
                }
            },
            SourceInputs(tempDirectory, new NetworkSecrets { WifiPassphrase = "super-secret-passphrase" }),
            tempDirectory.Path,
            protection);

        AssertAsset(bundle, tempDirectory.Path, @"Network\Certificates\Wired\wired.cer", @"Foundry\Config\Network\Certificates\Wired\wired.cer");
        AssertAsset(bundle, tempDirectory.Path, @"Network\Certificates\Wifi\wifi.cer", @"Foundry\Config\Network\Certificates\Wifi\wifi.cer");
        Assert.Equal([@"Network\Certificates\Wired\wired.cer"], bundle.Configuration.Dot1x.TrustedRootCertificates);
        Assert.Equal([@"Network\Certificates\Wifi\wifi.cer"], bundle.Configuration.Wifi.TrustedRootCertificates);
    }

    [Fact]
    public void CreateProvisioningBundle_WhenPfxCertificatePasswordsAreProvided_WritesDeploymentKeyedPasswordSecrets()
    {
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial protection = Protected();

        FoundryConnectProvisioningBundle bundle = new ConnectConfigurationGenerator().CreateProvisioningBundle(
            NetworkFixtureSources.Create(tempDirectory.Path, enterpriseWifi: true),
            SourceInputs(tempDirectory, NetworkFixtureSources.Secrets(enterpriseWifi: true)),
            tempDirectory.Path,
            protection);

        ConnectClientCertificateSettings wired = bundle.Configuration.Dot1x.ClientCertificate!;
        ConnectClientCertificateSettings wifi = bundle.Configuration.Wifi.ClientCertificate!;
        Assert.Equal(@"Network\Certificates\Wired\wired.pfx.encrypted", wired.Path);
        Assert.Equal(@"Network\Certificates\Wifi\wifi.pfx.encrypted", wifi.Path);
        Assert.True(wifi.IsContentProtected);
        Assert.Equal("wired-password", MediaSecretEnvelopeProtector.DecryptString(wired.PasswordSecret!, protection.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId));
        Assert.Equal("wifi-password", MediaSecretEnvelopeProtector.DecryptString(wifi.PasswordSecret!, protection.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId));
        Assert.Equal("wifi-pfx", Encoding.UTF8.GetString(DecryptStagedAsset(bundle, @"Foundry\Config\Network\Certificates\Wifi\wifi.pfx.encrypted", protection)));
        Assert.DoesNotContain("wired-password", ConnectJson(bundle), StringComparison.Ordinal);
        Assert.DoesNotContain("wifi-password", ConnectJson(bundle), StringComparison.Ordinal);
        Assert.Null(bundle.Configuration.Dot1x.CertificatePath);
        Assert.Null(bundle.Configuration.Wifi.CertificatePfxPasswordSecret);
    }

    [Fact]
    public void CreateProvisioningBundle_WhenPfxPasswordIsEmpty_StagesTheEncryptedPfxWithoutPasswordEnvelope()
    {
        // The envelope format refuses an empty ciphertext, so an empty password must not become an undecryptable envelope.
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial protection = Protected();

        FoundryConnectProvisioningBundle bundle = new ConnectConfigurationGenerator().CreateProvisioningBundle(
            NetworkFixtureSources.Create(tempDirectory.Path, enterpriseWifi: true),
            SourceInputs(tempDirectory, new NetworkSecrets { WiredCertificatePfxPassword = string.Empty }),
            tempDirectory.Path,
            protection);

        ConnectClientCertificateSettings wired = bundle.Configuration.Dot1x.ClientCertificate!;
        Assert.True(wired.IsContentProtected);
        Assert.Null(wired.PasswordSecret);
        Assert.Null(bundle.Configuration.Wifi.ClientCertificate!.PasswordSecret);
        Assert.Equal("wired-pfx", Encoding.UTF8.GetString(DecryptStagedAsset(bundle, @"Foundry\Config\Network\Certificates\Wired\wired.pfx.encrypted", protection)));
    }

    [Fact]
    public void Generate_ClientCertificate_IsStagedEncryptedWithTheDeploymentKey()
    {
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial protection = Protected();

        FoundryConnectProvisioningBundle bundle = new ConnectConfigurationGenerator().CreateProvisioningBundle(
            NetworkFixtureSources.Create(tempDirectory.Path, enterpriseWifi: false), SourceInputs(tempDirectory, NetworkFixtureSources.Secrets(false)),
            tempDirectory.Path, protection);

        ConnectClientCertificateSettings client = bundle.Configuration.Dot1x.ClientCertificate!;
        Assert.True(client.IsContentProtected);
        Assert.EndsWith(".pfx.encrypted", client.Path, StringComparison.Ordinal);
        Assert.DoesNotContain(bundle.AssetFiles, asset => asset.RelativeDestinationPath.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase));
        FoundryConnectProvisionedAssetFile staged = Assert.Single(bundle.AssetFiles, asset => asset.RelativeDestinationPath.EndsWith(".pfx.encrypted", StringComparison.Ordinal));
        SecretEnvelope envelope = JsonSerializer.Deserialize<SecretEnvelope>(File.ReadAllText(staged.SourcePath), ConfigurationJsonDefaults.SerializerOptions)!;
        Assert.Equal("wired-pfx", Encoding.UTF8.GetString(MediaSecretEnvelopeProtector.DecryptBytes(envelope, protection.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId)));
        Assert.Equal("wired-password", MediaSecretEnvelopeProtector.DecryptString(client.PasswordSecret!, protection.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId));
        Assert.Equal("personal-passphrase", MediaSecretEnvelopeProtector.DecryptString(bundle.Configuration.Wifi.PassphraseSecret!, protection.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId));
        Assert.False(MediaSecretEnvelopeProtector.HasEncryptedSecrets(ConnectJson(bundle)));
    }

    [Fact]
    public void Generate_NetworkSecretWithoutProtection_IsRefused()
    {
        using var tempDirectory = new TemporaryDirectory();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new ConnectConfigurationGenerator().CreateProvisioningBundle(
            NetworkFixtureSources.Create(tempDirectory.Path, enterpriseWifi: false), SourceInputs(tempDirectory, NetworkFixtureSources.Secrets(false)), tempDirectory.Path));

        Assert.Contains("MediaProtectionRequired", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_NetworkSecretWithUnprotectedMaterial_IsRefused()
    {
        // Unprotected media stores its deployment key in plaintext, so that key must never protect a network secret.
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial unprotected = DeploymentMediaProtectionService.CreateUnprotected();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new ConnectConfigurationGenerator().CreateProvisioningBundle(
            NetworkFixtureSources.Create(tempDirectory.Path, enterpriseWifi: false), SourceInputs(tempDirectory, NetworkFixtureSources.Secrets(false)), tempDirectory.Path, unprotected));

        Assert.Contains("MediaProtectionRequired", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_TrustedRootsOnly_NeedNoProtection()
    {
        using var tempDirectory = new TemporaryDirectory();
        string root = tempDirectory.CreateFile("source", "root.cer", "root");
        FoundryConfigurationDocument document = new()
        {
            Network = new NetworkSettings
            {
                Dot1x = new Dot1xSettings
                {
                    IsEnabled = true,
                    ProfileTemplate = TestFileReferences.For(tempDirectory.CreateFile("source", "wired.xml", "<LANProfile />")),
                    RequiresCertificate = true,
                    TrustedRootCertificates = [TestFileReferences.For(root)]
                }
            }
        };

        FoundryConnectProvisioningBundle bundle = new ConnectConfigurationGenerator().CreateProvisioningBundle(document, SourceInputs(tempDirectory), tempDirectory.Path);

        Assert.Equal([@"Network\Certificates\Wired\root.cer"], bundle.Configuration.Dot1x.TrustedRootCertificates);
        Assert.Null(bundle.Configuration.Dot1x.ClientCertificate);
    }

    [Fact]
    public void Generate_EnterpriseTemplateWithKeyMaterial_IsEncrypted()
    {
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial protection = Protected();
        FoundryConfigurationDocument source = NetworkFixtureSources.Create(tempDirectory.Path, enterpriseWifi: true);
        string template = tempDirectory.CreateFile("source", "keyed.xml",
            """<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1"><name>Corp</name><MSM><security><sharedKey><keyMaterial>secret</keyMaterial></sharedKey></security></MSM></WLANProfile>""");
        source = source with { Network = source.Network with { Wifi = source.Network.Wifi with { EnterpriseProfileTemplate = TestFileReferences.For(template) } } };

        FoundryConnectProvisioningBundle bundle = new ConnectConfigurationGenerator().CreateProvisioningBundle(
            source, SourceInputs(tempDirectory, NetworkFixtureSources.Secrets(true)), tempDirectory.Path, protection);

        Assert.True(bundle.Configuration.Wifi.IsEnterpriseProfileTemplateProtected);
        Assert.EndsWith(".xml.encrypted", bundle.Configuration.Wifi.EnterpriseProfileTemplatePath, StringComparison.Ordinal);
        Assert.DoesNotContain(bundle.AssetFiles, asset => File.ReadAllText(asset.SourcePath).Contains("secret", StringComparison.Ordinal));
        Assert.Equal(File.ReadAllBytes(template), DecryptStagedAsset(bundle, @"Foundry\Config\Network\Wifi\Profiles\keyed.xml.encrypted", protection));
    }

    [Fact]
    public void Generate_DisabledFeatures_WriteNoSecretOrAsset()
    {
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial protection = Protected();
        FoundryConfigurationDocument source = NetworkFixtureSources.Create(tempDirectory.Path, enterpriseWifi: false);
        source = source with { Network = source.Network with { Dot1x = source.Network.Dot1x with { IsEnabled = false }, Wifi = source.Network.Wifi with { IsEnabled = false } } };

        FoundryConnectProvisioningBundle bundle = new ConnectConfigurationGenerator().CreateProvisioningBundle(
            source, SourceInputs(tempDirectory, NetworkFixtureSources.Secrets(false)), tempDirectory.Path, protection);

        Assert.Empty(bundle.AssetFiles);
        Assert.Null(bundle.Configuration.Dot1x.ClientCertificate);
        Assert.Null(bundle.Configuration.Wifi.PassphraseSecret);
        Assert.DoesNotContain("\"kind\"", ConnectJson(bundle), StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_TrustedRootsWithTheSameFileName_AreStagedUnderDistinctNames()
    {
        using var tempDirectory = new TemporaryDirectory();
        string profile = tempDirectory.CreateFile("source", "wired.xml", "<LANProfile />");
        string firstRoot = tempDirectory.CreateFile("first", "root.cer", "first-root");
        string secondRoot = tempDirectory.CreateFile("second", "ROOT.cer", "second-root");
        FoundryConfigurationDocument document = new()
        {
            Network = new NetworkSettings
            {
                Dot1x = new Dot1xSettings
                {
                    IsEnabled = true,
                    ProfileTemplate = TestFileReferences.For(profile),
                    TrustedRootCertificates = [TestFileReferences.For(firstRoot), TestFileReferences.For(secondRoot)]
                }
            }
        };

        FoundryConnectProvisioningBundle bundle = new ConnectConfigurationGenerator().CreateProvisioningBundle(
            document, TestFileReferences.Resolving(profile, firstRoot, secondRoot), tempDirectory.Path);

        Assert.Equal([@"Network\Certificates\Wired\root.cer", @"Network\Certificates\Wired\ROOT-2.cer"], bundle.Configuration.Dot1x.TrustedRootCertificates);
        Assert.Equal(bundle.AssetFiles.Count, bundle.AssetFiles.Select(asset => asset.RelativeDestinationPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("first-root", File.ReadAllText(StagedSourcePath(bundle, @"Foundry\Config\Network\Certificates\Wired\root.cer")));
        Assert.Equal("second-root", File.ReadAllText(StagedSourcePath(bundle, @"Foundry\Config\Network\Certificates\Wired\ROOT-2.cer")));
    }

    [Fact]
    public void Generate_EnterpriseTemplateWithKeyMaterialOnUnprotectedMedia_IsStagedPlain()
    {
        using var tempDirectory = new TemporaryDirectory();
        string template = tempDirectory.CreateFile("source", "keyed.xml", KeyedEnterpriseTemplate);
        FoundryConfigurationDocument document = EnterpriseWifiWithoutCertificates(template);

        FoundryConnectProvisioningBundle bundle = new ConnectConfigurationGenerator().CreateProvisioningBundle(
            document, SourceInputs(tempDirectory), tempDirectory.Path);

        Assert.False(bundle.Configuration.Wifi.IsEnterpriseProfileTemplateProtected);
        Assert.Equal(@"Network\Wifi\Profiles\keyed.xml", bundle.Configuration.Wifi.EnterpriseProfileTemplatePath);
        Assert.Equal(KeyedEnterpriseTemplate, File.ReadAllText(StagedSourcePath(bundle, @"Foundry\Config\Network\Wifi\Profiles\keyed.xml")));
    }

    [Fact]
    public void Generate_UnreadableEnterpriseTemplateOnProtectedMedia_IsEncrypted()
    {
        // A template the reader refuses (here a prohibited DTD) may still hide key material, so protected media encrypt it.
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial protection = Protected();
        string template = tempDirectory.CreateFile("source", "dtd.xml",
            """<!DOCTYPE WLANProfile [<!ENTITY k "secret">]><WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1"><name>&k;</name></WLANProfile>""");

        FoundryConnectProvisioningBundle bundle = new ConnectConfigurationGenerator().CreateProvisioningBundle(
            EnterpriseWifiWithoutCertificates(template), SourceInputs(tempDirectory), tempDirectory.Path, protection);

        Assert.True(bundle.Configuration.Wifi.IsEnterpriseProfileTemplateProtected);
        Assert.Equal(@"Network\Wifi\Profiles\dtd.xml.encrypted", bundle.Configuration.Wifi.EnterpriseProfileTemplatePath);
        Assert.Equal(File.ReadAllBytes(template), DecryptStagedAsset(bundle, @"Foundry\Config\Network\Wifi\Profiles\dtd.xml.encrypted", protection));
    }

    [Fact]
    public void Generate_StaleClientCertificateOnPersonalWifi_IsNotStaged()
    {
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial protection = Protected();
        FoundryConfigurationDocument source = NetworkFixtureSources.Create(tempDirectory.Path, enterpriseWifi: false);
        AuthoringFileReference stalePfx = TestFileReferences.For(tempDirectory.CreateFile("source", "stale.pfx", "stale-pfx"));
        source = source with { Network = source.Network with { Wifi = source.Network.Wifi with { ClientCertificate = stalePfx } } };

        FoundryConnectProvisioningBundle bundle = new ConnectConfigurationGenerator().CreateProvisioningBundle(
            source,
            SourceInputs(tempDirectory, NetworkFixtureSources.Secrets(false) with { WifiCertificatePfxPassword = "stale-password" }),
            tempDirectory.Path,
            protection);

        Assert.Null(bundle.Configuration.Wifi.ClientCertificate);
        Assert.NotNull(bundle.Configuration.Wifi.PassphraseSecret);
        Assert.DoesNotContain(bundle.AssetFiles, asset => asset.RelativeDestinationPath.Contains(@"\Wifi\", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_PersonalWifiWithAStaleEnterpriseProfile_StagesThePassphraseOnly()
    {
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial protection = Protected();
        FoundryConfigurationDocument source = NetworkFixtureSources.Create(tempDirectory.Path, enterpriseWifi: false);
        AuthoringFileReference staleTemplate = TestFileReferences.For(tempDirectory.CreateFile("source", "stale-wifi.xml", "<WLANProfile />"));
        source = source with
        {
            Network = source.Network with { Wifi = source.Network.Wifi with { HasEnterpriseProfile = true, EnterpriseProfileTemplate = staleTemplate } }
        };

        FoundryConnectProvisioningBundle bundle = new ConnectConfigurationGenerator().CreateProvisioningBundle(
            source,
            SourceInputs(tempDirectory, NetworkFixtureSources.Secrets(false)),
            tempDirectory.Path,
            protection);

        Assert.NotNull(bundle.Configuration.Wifi.PassphraseSecret);
        Assert.Equal(
            "personal-passphrase",
            MediaSecretEnvelopeProtector.DecryptString(bundle.Configuration.Wifi.PassphraseSecret!, protection.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId));
        Assert.False(bundle.Configuration.Wifi.HasEnterpriseProfile);
        Assert.Null(bundle.Configuration.Wifi.EnterpriseProfileTemplatePath);
        Assert.DoesNotContain(bundle.AssetFiles, asset => asset.RelativeDestinationPath.Contains(@"\Wifi\", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_StalePassphraseOnEnterpriseWifi_IsNotStaged()
    {
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial protection = Protected();

        FoundryConnectProvisioningBundle bundle = new ConnectConfigurationGenerator().CreateProvisioningBundle(
            NetworkFixtureSources.Create(tempDirectory.Path, enterpriseWifi: true),
            SourceInputs(tempDirectory, NetworkFixtureSources.Secrets(true) with { WifiPassphrase = "stale-passphrase" }),
            tempDirectory.Path,
            protection);

        Assert.Null(bundle.Configuration.Wifi.PassphraseSecret);
        Assert.NotNull(bundle.Configuration.Wifi.ClientCertificate);
    }

    [Fact]
    public void Generate_EncryptedAssetOverTheConnectLimit_IsRefusedWithoutNamingTheFile()
    {
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial protection = Protected();
        FoundryConfigurationDocument source = NetworkFixtureSources.Create(tempDirectory.Path, enterpriseWifi: false);
        // Base64URL makes the envelope about 4/3 of the source: 3.2 MB of PFX exceeds the 4 MiB envelope limit.
        File.WriteAllBytes(System.IO.Path.Combine(tempDirectory.Path, "source", "wired.pfx"), new byte[3_200_000]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new ConnectConfigurationGenerator().CreateProvisioningBundle(
            source, SourceInputs(tempDirectory, NetworkFixtureSources.Secrets(false)), tempDirectory.Path, protection));

        Assert.Contains(ConnectProtectedFileLimits.MaxEnvelopeFileBytes.ToString(System.Globalization.CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("wired.pfx", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_EncryptedAssetUnderTheConnectLimit_IsStagedWithinIt()
    {
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial protection = Protected();
        FoundryConfigurationDocument source = NetworkFixtureSources.Create(tempDirectory.Path, enterpriseWifi: false);
        File.WriteAllBytes(System.IO.Path.Combine(tempDirectory.Path, "source", "wired.pfx"), new byte[3_000_000]);

        FoundryConnectProvisioningBundle bundle = new ConnectConfigurationGenerator().CreateProvisioningBundle(
            source, SourceInputs(tempDirectory, NetworkFixtureSources.Secrets(false)), tempDirectory.Path, protection);

        string staged = StagedSourcePath(bundle, @"Foundry\Config\Network\Certificates\Wired\wired.pfx.encrypted");
        Assert.InRange(new FileInfo(staged).Length, 4_000_000, ConnectProtectedFileLimits.MaxEnvelopeFileBytes);
    }

    [Fact]
    public void Generate_EmptyEnterpriseTemplateOnProtectedMedia_IsRefusedClearly()
    {
        using var tempDirectory = new TemporaryDirectory();
        using DeploymentMediaProtectionMaterial protection = Protected();
        string template = tempDirectory.CreateFile("source", "empty.xml", string.Empty);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new ConnectConfigurationGenerator().CreateProvisioningBundle(
            EnterpriseWifiWithoutCertificates(template), SourceInputs(tempDirectory), tempDirectory.Path, protection));

        Assert.Contains("empty", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("empty.xml", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private const string KeyedEnterpriseTemplate =
        """<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1"><name>Corp</name><MSM><security><sharedKey><keyMaterial>secret</keyMaterial></sharedKey></security></MSM></WLANProfile>""";

    // Enterprise Wi-Fi with only a template: no client certificate, so the media needs no protection.
    private static FoundryConfigurationDocument EnterpriseWifiWithoutCertificates(string templatePath) => new()
    {
        Network = new NetworkSettings
        {
            WifiProvisioned = true,
            Wifi = new WifiSettings
            {
                IsEnabled = true,
                Ssid = "Corp WiFi",
                SecurityType = NetworkConfigurationValidator.WifiSecurityEnterprise,
                HasEnterpriseProfile = true,
                EnterpriseProfileTemplate = TestFileReferences.For(templatePath)
            }
        }
    };

    // The connect section as the WinPE serializer writes it.
    private static string ConnectJson(FoundryConnectProvisioningBundle bundle)
    {
        string json = WinPeConfigurationGenerator.Serialize(new FoundryWinPeConfigurationDocument { Connect = bundle.Configuration });
        return System.Text.Json.Nodes.JsonNode.Parse(json)!["connect"]!.ToJsonString(ConfigurationJsonDefaults.SerializerOptions);
    }

    private static string StagedSourcePath(FoundryConnectProvisioningBundle bundle, string relativeDestinationPath) =>
        Assert.Single(bundle.AssetFiles, asset => string.Equals(asset.RelativeDestinationPath, relativeDestinationPath, StringComparison.Ordinal)).SourcePath;

    private static DeploymentMediaProtectionMaterial Protected() => DeploymentMediaProtectionService.CreateProtected(FixturePassword);

    // References name files created in the "source" folder; the generator reads them through the resolver only.
    private static MediaInputs SourceInputs(TemporaryDirectory tempDirectory, NetworkSecrets? secrets = null) => new()
    {
        ResolveFile = reference => System.IO.Path.Combine(tempDirectory.Path, "source", reference.FileName) is var path && File.Exists(path) ? path : null,
        Network = secrets ?? new NetworkSecrets()
    };

    private static byte[] DecryptStagedAsset(FoundryConnectProvisioningBundle bundle, string relativeDestinationPath, DeploymentMediaProtectionMaterial protection)
    {
        FoundryConnectProvisionedAssetFile staged = Assert.Single(
            bundle.AssetFiles,
            asset => string.Equals(asset.RelativeDestinationPath, relativeDestinationPath, StringComparison.Ordinal));
        SecretEnvelope envelope = JsonSerializer.Deserialize<SecretEnvelope>(File.ReadAllText(staged.SourcePath), ConfigurationJsonDefaults.SerializerOptions)!;
        return MediaSecretEnvelopeProtector.DecryptBytes(envelope, protection.DeploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId);
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
