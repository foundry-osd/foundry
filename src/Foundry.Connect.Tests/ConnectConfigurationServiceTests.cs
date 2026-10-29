// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Nodes;
using Foundry.Connect.Configuration;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Runtime;
using Foundry.Core.Services.Configuration;
using Foundry.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CoreConfiguration = Foundry.Core.Models.Configuration;

namespace Foundry.Connect.Tests;

public sealed class ConnectConfigurationServiceTests
{
    private static readonly BootMediaRuntimeContext ProductionRuntime = new("26.10.3.2", true, false, false);

    [Fact]
    public void Load_WhenNoConfigurationFileIsAvailable_ReturnsNormalizedDefaults()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        var service = new ConnectConfigurationService([], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfigurationDocument configuration = LoadConfiguration(service);

        Assert.False(service.IsLoadedFromDisk);
        Assert.Null(service.ConfigurationPath);
        Assert.Equal(FoundryConnectConfigurationDocument.CurrentSchemaVersion, configuration.SchemaVersion);
        Assert.Equal(5, configuration.InternetProbe.TimeoutSeconds);
        Assert.Equal(
            ["http://www.msftconnecttest.com/connecttest.txt", "http://www.google.com"],
            configuration.InternetProbe.ProbeUris);
    }

    [Fact]
    public void Load_WhenConfigurationFileContainsMixedProbeUris_NormalizesValues()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "normalized.json",
            """
            {
              "schemaVersion": 5,
              "internetProbe": {
                "probeUris": [
                  " https://example.com/health ",
                  "invalid-uri",
                  "https://example.com/health",
                  "http://contoso.test/connect"
                ],
                "timeoutSeconds": 99
              }
            }
            """);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfigurationDocument configuration = LoadConfiguration(service);

        Assert.True(service.IsLoadedFromDisk);
        Assert.Equal(System.IO.Path.GetFullPath(configurationPath), service.ConfigurationPath);
        Assert.Equal(30, configuration.InternetProbe.TimeoutSeconds);
        Assert.Equal(
            ["https://example.com/health", "http://contoso.test/connect"],
            configuration.InternetProbe.ProbeUris);
    }

    [Fact]
    public void Load_RecommendsRebuildForOlderAuthoringReleaseWithCurrentSchema()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", $$"""{"schemaVersion":{{FoundryConnectConfigurationDocument.CurrentSchemaVersion}},"authoringVersion":"26.10.3.1"}""");
        var service = new ConnectConfigurationService(["--config", path], NullLogger<ConnectConfigurationService>.Instance, ProductionRuntime);

        Assert.True(service.Load().IsSuccess);

        Assert.Equal(BootMediaUpdateReason.NewerRelease, service.BootMediaUpdateReason);
        Assert.True(service.IsBootMediaUpdateRecommended);
    }

    [Fact]
    public void Load_UsesLegacyFallbackWithoutAuthoringMetadata()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", """{"schemaVersion":5}""");
        var service = new ConnectConfigurationService(["--config", path], NullLogger<ConnectConfigurationService>.Instance, ProductionRuntime);

        Assert.True(service.Load().IsSuccess);

        Assert.Equal(BootMediaUpdateReason.UnknownAuthoringVersion, service.BootMediaUpdateReason);
        Assert.True(service.IsBootMediaUpdateRecommended);
    }

    [Theory]
    [InlineData("26.10.3.2", false, false, false, "release")]
    [InlineData("26.10.3.2", true, true, false, "release")]
    [InlineData("26.10.3.2", true, false, true, "release")]
    [InlineData("1.0.0.0", true, false, false, "release")]
    [InlineData("26.10.3.2", true, false, false, " Debug ")]
    public void Load_SuppressesAdviceOutsideProductionWinPe(string version, bool winPe, bool debugger, bool debugBuild, string source)
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", """{"schemaVersion":5,"authoringVersion":"26.10.3.1"}""");
        File.WriteAllText(System.IO.Path.Combine(directory.Path, "foundry.connect.provisioning-source.txt"), source);
        var service = new ConnectConfigurationService(["--config", path], NullLogger<ConnectConfigurationService>.Instance, new BootMediaRuntimeContext(version, winPe, debugger, debugBuild));

        Assert.True(service.Load().IsSuccess);

        Assert.Equal(BootMediaUpdateReason.None, service.BootMediaUpdateReason);
        Assert.False(service.IsBootMediaUpdateRecommended);
    }

    [Theory]
    [InlineData("debug", BootMediaUpdateReason.None)]
    [InlineData("unknown", BootMediaUpdateReason.UnknownAuthoringVersion)]
    public void Load_OnlyExplicitDebugTelemetrySuppressesLegacyAdvice(string source, BootMediaUpdateReason expected)
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", $$$"""{"telemetry":{"runtimePayloadSource":"{{{source}}}"}}""");
        var service = new ConnectConfigurationService(["--config", path], NullLogger<ConnectConfigurationService>.Instance, ProductionRuntime);

        Assert.True(service.Load().IsSuccess);

        Assert.Equal(expected, service.BootMediaUpdateReason);
    }

    [Theory]
    [InlineData("26.10.3.2")]
    [InlineData("26.10.4.1")]
    public void Load_DoesNotRecommendRebuildForSameOrNewerAuthor(string author)
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", $$"""{"schemaVersion":5,"authoringVersion":"{{author}}"}""");
        var service = new ConnectConfigurationService(["--config", path], NullLogger<ConnectConfigurationService>.Instance, ProductionRuntime);

        Assert.True(service.Load().IsSuccess);

        Assert.Equal(BootMediaUpdateReason.None, service.BootMediaUpdateReason);
        Assert.False(service.IsBootMediaUpdateRecommended);
    }

    [Fact]
    public void Load_UsesLegacyFallbackWhenProvisioningMarkerIsUnreadable()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", """{"schemaVersion":5}""");
        Directory.CreateDirectory(System.IO.Path.Combine(directory.Path, "foundry.connect.provisioning-source.txt"));
        var service = new ConnectConfigurationService(["--config", path], NullLogger<ConnectConfigurationService>.Instance, ProductionRuntime);

        Assert.True(service.Load().IsSuccess);

        Assert.Equal(BootMediaUpdateReason.UnknownAuthoringVersion, service.BootMediaUpdateReason);
    }

    [Fact]
    public void Load_ClearsPreviousAdviceWhenReloadFails()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", """{"schemaVersion":5}""");
        var service = new ConnectConfigurationService(["--config", path], NullLogger<ConnectConfigurationService>.Instance, ProductionRuntime);
        Assert.True(service.Load().IsSuccess);
        Assert.True(service.IsBootMediaUpdateRecommended);
        File.WriteAllText(path, "{invalid}");

        Assert.False(service.Load().IsSuccess);

        Assert.Equal(BootMediaUpdateReason.None, service.BootMediaUpdateReason);
        Assert.False(service.IsBootMediaUpdateRecommended);
    }

    [Fact]
    public void Load_WhenConfigurationContainsTelemetry_PreservesTelemetrySettings()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "telemetry.json",
            $$"""
            {
              "schemaVersion": {{FoundryConnectConfigurationDocument.CurrentSchemaVersion}},
              "telemetry": {
                "isEnabled": false,
                "isRemoteDiagnosticsEnabled": true,
                "installId": "install-id",
                "hostUrl": "https://eu.i.posthog.com",
                "projectToken": "project-token",
                "runtimePayloadSource": "debug"
              }
            }
            """);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfigurationDocument configuration = LoadConfiguration(service);

        Assert.False(configuration.Telemetry.IsEnabled);
        Assert.True(configuration.Telemetry.IsRemoteDiagnosticsEnabled);
        Assert.Equal("install-id", configuration.Telemetry.InstallId);
        Assert.Equal(TelemetryDefaults.PostHogEuHost, configuration.Telemetry.HostUrl);
        Assert.Equal("project-token", configuration.Telemetry.ProjectToken);
        Assert.Equal(TelemetryRuntimePayloadSources.Debug, configuration.Telemetry.RuntimePayloadSource);
    }

    [Fact]
    public void Load_WhenRemoteDiagnosticsConsentIsMissing_DefaultsToEnabled()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "legacy-telemetry.json",
            """{ "schemaVersion": 3, "telemetry": { "isEnabled": true } }""");
        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfigurationDocument configuration = LoadConfiguration(service);

        Assert.True(configuration.Telemetry.IsRemoteDiagnosticsEnabled);
    }

    [Fact]
    public void Load_WhenEnvironmentVariableIsSet_TakesPrecedenceOverCommandLineArgument()
    {
        using var tempDirectory = new TemporaryDirectory();
        string environmentConfigurationPath = CreateJsonFile(tempDirectory.Path, "environment.json", """{ "schemaVersion": 5 }""");
        string commandLineConfigurationPath = CreateJsonFile(tempDirectory.Path, "argument.json", """{ "schemaVersion": 2 }""");
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", environmentConfigurationPath);

        var service = new ConnectConfigurationService(["--config", commandLineConfigurationPath], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfigurationDocument configuration = LoadConfiguration(service);

        Assert.True(service.IsLoadedFromDisk);
        Assert.Equal(System.IO.Path.GetFullPath(environmentConfigurationPath), service.ConfigurationPath);
        Assert.Equal(5, configuration.SchemaVersion);
    }

    [Fact]
    public void Load_WhenCoreGeneratedConfigurationIsProvided_PreservesEffectiveNetworkPaths()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string wiredProfilePath = CreateFile(tempDirectory.Path, "wired.xml", "<LANProfile />");
        string configurationJson = new ConnectConfigurationGenerator().CreateProvisioningBundle(
            new CoreConfiguration.FoundryConfigurationDocument
            {
                Network = new CoreConfiguration.NetworkSettings
                {
                    Dot1x = new CoreConfiguration.Dot1xSettings
                    {
                        IsEnabled = true,
                        ProfileTemplate = Reference(wiredProfilePath)
                    }
                }
            },
            Inputs(tempDirectory.Path),
            tempDirectory.Path).ConfigurationJson;
        string configurationPath = CreateJsonFile(tempDirectory.Path, "core-generated.json", configurationJson);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfigurationDocument configuration = LoadConfiguration(service);

        Assert.True(service.IsLoadedFromDisk);
        Assert.Equal(CoreConfiguration.FoundryConnectConfigurationDocument.CurrentSchemaVersion, configuration.SchemaVersion);
        Assert.Equal(@"Network\Wired\Profiles\wired.xml", configuration.Dot1x.ProfileTemplatePath);
        Assert.NotNull(configuration.Capabilities);
        Assert.NotNull(configuration.Wifi);
        Assert.NotNull(configuration.InternetProbe);
    }

    [Fact]
    public void Load_WhenCoreGeneratedConfigurationContainsNetworkProfileRoaming_PreservesOptIn()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationJson = new ConnectConfigurationGenerator().CreateProvisioningBundle(
            new CoreConfiguration.FoundryConfigurationDocument
            {
                Network = new CoreConfiguration.NetworkSettings
                {
                    RoamWiredDot1xProfileToWindows = true,
                    RoamWiredDot1xPrivateKeyMaterialToWindows = true,
                    RoamWifiProfileToWindows = false
                }
            },
            MediaInputs.None,
            tempDirectory.Path).ConfigurationJson;
        string configurationPath = CreateJsonFile(tempDirectory.Path, "core-generated.json", configurationJson);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfigurationDocument configuration = LoadConfiguration(service);

        Assert.True(configuration.Network.ProfileRoaming.WiredDot1x.IsEnabled);
        Assert.True(configuration.Network.ProfileRoaming.WiredDot1x.IncludePrivateKeyMaterial);
        Assert.False(configuration.Network.ProfileRoaming.Wifi.IsEnabled);
    }

    [Fact]
    public void Load_WhenOnlyLegacyProfileRoamingSettingsAreProvided_MigratesBothTransports()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "legacy-roaming.json",
            """
            {
              "network": {
                "profileRoaming": {
                  "isEnabled": true,
                  "includePrivateKeyMaterial": true
                }
              }
            }
            """);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfigurationDocument configuration = LoadConfiguration(service);

        Assert.True(configuration.Network.ProfileRoaming.WiredDot1x.IsEnabled);
        Assert.True(configuration.Network.ProfileRoaming.WiredDot1x.IncludePrivateKeyMaterial);
        Assert.True(configuration.Network.ProfileRoaming.Wifi.IsEnabled);
        Assert.True(configuration.Network.ProfileRoaming.Wifi.IncludePrivateKeyMaterial);
    }

    [Fact]
    public void Load_WhenSplitRoamingSettingsArePartial_BackfillsOnlyMissingFieldsFromLegacySettings()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "partial-roaming.json",
            """
            {
              "network": {
                "profileRoaming": {
                  "wiredDot1x": {
                    "isEnabled": true
                  },
                  "wifi": {
                    "includePrivateKeyMaterial": false
                  },
                  "isEnabled": true,
                  "includePrivateKeyMaterial": true
                }
              }
            }
            """);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfigurationDocument configuration = LoadConfiguration(service);

        Assert.True(configuration.Network.ProfileRoaming.WiredDot1x.IsEnabled);
        Assert.True(configuration.Network.ProfileRoaming.WiredDot1x.IncludePrivateKeyMaterial);
        Assert.True(configuration.Network.ProfileRoaming.Wifi.IsEnabled);
        Assert.False(configuration.Network.ProfileRoaming.Wifi.IncludePrivateKeyMaterial);
    }

    [Fact]
    public void Load_WhenSplitRoamingSettingsAreExplicit_DoesNotLetLegacyFallbackOverrideThem()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "mixed-roaming.json",
            """
            {
              "schemaVersion": 3,
              "network": {
                "profileRoaming": {
                  "wiredDot1x": {
                    "isEnabled": false,
                    "includePrivateKeyMaterial": false
                  },
                  "wifi": {
                    "isEnabled": false,
                    "includePrivateKeyMaterial": false
                  },
                  "isEnabled": true,
                  "includePrivateKeyMaterial": true
                }
              }
            }
            """);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfigurationDocument configuration = LoadConfiguration(service);

        Assert.False(configuration.Network.ProfileRoaming.WiredDot1x.IsEnabled);
        Assert.False(configuration.Network.ProfileRoaming.WiredDot1x.IncludePrivateKeyMaterial);
        Assert.False(configuration.Network.ProfileRoaming.Wifi.IsEnabled);
        Assert.False(configuration.Network.ProfileRoaming.Wifi.IncludePrivateKeyMaterial);
    }

    [Fact]
    public void Load_WhenCoreGeneratedConfigurationContainsEncryptedPassphrase_DecryptsPassphraseFromMediaKey()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        Foundry.Core.Models.Configuration.FoundryConnectProvisioningBundle bundle =
            new ConnectConfigurationGenerator().CreateProvisioningBundle(
                new CoreConfiguration.FoundryConfigurationDocument
                {
                    Network = new CoreConfiguration.NetworkSettings
                    {
                        WifiProvisioned = true,
                        Wifi = new CoreConfiguration.WifiSettings
                        {
                            IsEnabled = true,
                            Ssid = "Corp WiFi",
                            SecurityType = "WPA2/WPA3-Personal"
                        }
                    }
                },
                Inputs(tempDirectory.Path, new NetworkSecrets { WifiPassphrase = "super-secret-passphrase" }),
                tempDirectory.Path, "26.10.3.1");
        Assert.NotNull(bundle.MediaSecretsKey);
        Assert.DoesNotContain("super-secret-passphrase", bundle.ConfigurationJson, StringComparison.Ordinal);

        string configDirectory = System.IO.Path.Combine(tempDirectory.Path, "Config");
        Directory.CreateDirectory(configDirectory);
        string configurationPath = CreateJsonFile(configDirectory, "foundry.connect.config.json", bundle.ConfigurationJson);
        CreateBinaryFile(System.IO.Path.Combine(configDirectory, "Secrets"), "media-secrets.key", bundle.MediaSecretsKey);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        (FoundryConnectConfigurationDocument configuration, ConnectRuntimeSecrets secrets) = LoadWithSecrets(service);

        Assert.Equal("26.10.3.1", configuration.AuthoringVersion);
        Assert.Equal("super-secret-passphrase", secrets.WifiPassphrase);
        Assert.Null(configuration.Wifi.PassphraseSecret);
    }

    [Fact]
    public void Load_WhenCoreGeneratedConfigurationContainsEncryptedPfxPasswords_DecryptsPasswordsFromMediaKey()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        Foundry.Core.Models.Configuration.FoundryConnectProvisioningBundle bundle =
            new ConnectConfigurationGenerator().CreateProvisioningBundle(
                new CoreConfiguration.FoundryConfigurationDocument
                {
                    Network = new CoreConfiguration.NetworkSettings
                    {
                        WifiProvisioned = true,
                        Dot1x = new CoreConfiguration.Dot1xSettings
                        {
                            IsEnabled = true,
                            ProfileTemplate = Reference(CreateFile(tempDirectory.Path, "wired.xml", "<LANProfile />")),
                            RequiresCertificate = true,
                            Certificate = Reference(CreateFile(tempDirectory.Path, "wired.pfx", "wired-pfx"))
                        },
                        Wifi = new CoreConfiguration.WifiSettings
                        {
                            IsEnabled = true,
                            Ssid = "Corp WiFi",
                            SecurityType = NetworkConfigurationValidator.WifiSecurityEnterprise,
                            HasEnterpriseProfile = true,
                            EnterpriseProfileTemplate = Reference(CreateFile(
                                tempDirectory.Path,
                                "wifi.xml",
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
                                """)),
                            RequiresCertificate = true,
                            Certificate = Reference(CreateFile(tempDirectory.Path, "wifi.pfx", "wifi-pfx"))
                        }
                    }
                },
                Inputs(tempDirectory.Path, new NetworkSecrets { WiredCertificatePfxPassword = "wired-password", WifiCertificatePfxPassword = "wifi-password" }),
                tempDirectory.Path);
        Assert.NotNull(bundle.MediaSecretsKey);
        Assert.DoesNotContain("wired-password", bundle.ConfigurationJson, StringComparison.Ordinal);
        Assert.DoesNotContain("wifi-password", bundle.ConfigurationJson, StringComparison.Ordinal);

        string configDirectory = System.IO.Path.Combine(tempDirectory.Path, "Config");
        Directory.CreateDirectory(configDirectory);
        string configurationPath = CreateJsonFile(configDirectory, "foundry.connect.config.json", bundle.ConfigurationJson);
        CreateBinaryFile(System.IO.Path.Combine(configDirectory, "Secrets"), "media-secrets.key", bundle.MediaSecretsKey);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        (FoundryConnectConfigurationDocument configuration, ConnectRuntimeSecrets secrets) = LoadWithSecrets(service);

        Assert.Equal("wired-password", secrets.WiredCertificatePfxPassword);
        Assert.Equal("wifi-password", secrets.WifiCertificatePfxPassword);
        Assert.NotNull(configuration.Dot1x.CertificatePfxPasswordSecret);
        Assert.NotNull(configuration.Wifi.CertificatePfxPasswordSecret);
    }

    [Fact]
    public void Load_WhenEncryptedPassphraseHasNoMediaKey_ReportsConfigurationFailure()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        Foundry.Core.Models.Configuration.FoundryConnectProvisioningBundle bundle =
            new ConnectConfigurationGenerator().CreateProvisioningBundle(
                new CoreConfiguration.FoundryConfigurationDocument
                {
                    Network = new CoreConfiguration.NetworkSettings
                    {
                        WifiProvisioned = true,
                        Wifi = new CoreConfiguration.WifiSettings
                        {
                            IsEnabled = true,
                            Ssid = "Corp WiFi",
                            SecurityType = "WPA2/WPA3-Personal"
                        }
                    }
                },
                Inputs(tempDirectory.Path, new NetworkSecrets { WifiPassphrase = "super-secret-passphrase" }),
                tempDirectory.Path);
        string configDirectory = System.IO.Path.Combine(tempDirectory.Path, "Config");
        Directory.CreateDirectory(configDirectory);
        string configurationPath = CreateJsonFile(configDirectory, "foundry.connect.config.json", bundle.ConfigurationJson);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        ConnectConfigurationLoadResult result = service.Load();
        Assert.False(result.IsSuccess);
        Assert.Null(result.Configuration);
        Assert.Null(result.Secrets);
        string failure = result.FailureMessage!;
        Assert.Contains("Media secret key file was not found", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WhenEncryptedPassphraseUsesUnsupportedEnvelope_ReportsConfigurationFailure()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        Foundry.Core.Models.Configuration.FoundryConnectProvisioningBundle bundle = CreatePersonalWifiProvisioningBundle(tempDirectory.Path);
        Assert.NotNull(bundle.MediaSecretsKey);

        string configDirectory = System.IO.Path.Combine(tempDirectory.Path, "Config");
        Directory.CreateDirectory(configDirectory);
        string configurationJson = ReplacePassphraseSecretProperty(bundle.ConfigurationJson, "algorithm", "unsupported");
        string configurationPath = CreateJsonFile(configDirectory, "foundry.connect.config.json", configurationJson);
        CreateBinaryFile(System.IO.Path.Combine(configDirectory, "Secrets"), "media-secrets.key", bundle.MediaSecretsKey);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        ConnectConfigurationLoadResult result = service.Load();
        Assert.False(result.IsSuccess);
        Assert.Null(result.Configuration);
        Assert.Null(result.Secrets);
        string failure = result.FailureMessage!;
        Assert.Contains("not supported", failure, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-passphrase", failure, StringComparison.Ordinal);
        Assert.DoesNotContain("unsupported", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WhenEncryptedPassphraseIsTampered_ReportsConfigurationFailure()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        Foundry.Core.Models.Configuration.FoundryConnectProvisioningBundle bundle = CreatePersonalWifiProvisioningBundle(tempDirectory.Path);
        Assert.NotNull(bundle.MediaSecretsKey);

        string configDirectory = System.IO.Path.Combine(tempDirectory.Path, "Config");
        Directory.CreateDirectory(configDirectory);
        string configurationJson = ReplacePassphraseSecretProperty(bundle.ConfigurationJson, "ciphertext", "AA");
        string configurationPath = CreateJsonFile(configDirectory, "foundry.connect.config.json", configurationJson);
        CreateBinaryFile(System.IO.Path.Combine(configDirectory, "Secrets"), "media-secrets.key", bundle.MediaSecretsKey);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        ConnectConfigurationLoadResult result = service.Load();
        Assert.False(result.IsSuccess);
        Assert.Null(result.Configuration);
        Assert.Null(result.Secrets);
        string failure = result.FailureMessage!;
        Assert.Contains("could not be decrypted", failure, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-passphrase", failure, StringComparison.Ordinal);
        Assert.DoesNotContain("AA", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WhenLegacyPlaintextPassphraseIsProvided_PreservesPassphrase()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "legacy.json",
            """
            {
              "schemaVersion": 1,
              "capabilities": {
                "wifiProvisioned": true
              },
              "wifi": {
                "isEnabled": true,
                "ssid": "Corp WiFi",
                "securityType": "WPA2/WPA3-Personal",
                "passphrase": "legacy-passphrase"
              }
            }
            """);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        (FoundryConnectConfigurationDocument configuration, ConnectRuntimeSecrets secrets) = LoadWithSecrets(service);

        Assert.Equal("legacy-passphrase", secrets.WifiPassphrase);
        Assert.Null(configuration.Wifi.PassphraseSecret);
    }

    [Fact]
    public void Load_SchemaFiveWithoutSecretsFolder_Succeeds()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "foundry.connect.config.json",
            """
            {
              "schemaVersion": 5,
              "capabilities": {
                "wifiProvisioned": true
              },
              "wifi": {
                "isEnabled": true,
                "ssid": "Open WiFi",
                "securityType": "Open"
              }
            }
            """);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfigurationDocument configuration = LoadConfiguration(service);

        Assert.True(service.IsLoadedFromDisk);
        Assert.False(Directory.Exists(System.IO.Path.Combine(tempDirectory.Path, "Secrets")));
        Assert.Equal(5, configuration.SchemaVersion);
        Assert.Equal("Open WiFi", configuration.Wifi.Ssid);
        Assert.False(configuration.Dot1x.IsEnabled);
        Assert.Equal(5, configuration.InternetProbe.TimeoutSeconds);
        Assert.Equal(
            ["http://www.msftconnecttest.com/connecttest.txt", "http://www.google.com"],
            configuration.InternetProbe.ProbeUris);
    }

    [Fact]
    public void Load_SchemaZero_IsConfigurationFailure()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", """{"schemaVersion":0}""");
        var service = new ConnectConfigurationService(["--config", path], NullLogger<ConnectConfigurationService>.Instance);

        ConnectConfigurationLoadResult result = service.Load();

        Assert.False(result.IsSuccess);
        Assert.Null(result.Configuration);
        Assert.Null(result.Secrets);
        Assert.Contains("schema version 0", result.FailureMessage, StringComparison.Ordinal);
        Assert.False(service.IsLoadedFromDisk);
    }

    [Fact]
    public void Load_NullTelemetry_UsesDefaults()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", """{"schemaVersion":5,"telemetry":null}""");
        var service = new ConnectConfigurationService(["--config", path], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfigurationDocument configuration = LoadConfiguration(service);

        Assert.Equal(new TelemetrySettings(), configuration.Telemetry);
    }

    [Fact]
    public void Load_NewerSchema_LoadsAndLogsWarning()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var directory = new TemporaryDirectory();
        int newerVersion = FoundryConnectConfigurationDocument.CurrentSchemaVersion + 1;
        string path = CreateJsonFile(directory.Path, "config.json", $$"""{"schemaVersion":{{newerVersion}}}""");
        var logger = new WarningRecordingLogger();
        var service = new ConnectConfigurationService(["--config", path], logger);

        FoundryConnectConfigurationDocument configuration = LoadConfiguration(service);

        Assert.Equal(newerVersion, configuration.SchemaVersion);
        Assert.True(service.IsLoadedFromDisk);
        Assert.Equal(1, logger.WarningCount);
    }

    [Fact]
    public void Load_WhenMediaSecretKeyHasInvalidLength_ReportsConfigurationFailure()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        Foundry.Core.Models.Configuration.FoundryConnectProvisioningBundle bundle = CreatePersonalWifiProvisioningBundle(tempDirectory.Path);
        string configDirectory = System.IO.Path.Combine(tempDirectory.Path, "Config");
        Directory.CreateDirectory(configDirectory);
        string configurationPath = CreateJsonFile(configDirectory, "foundry.connect.config.json", bundle.ConfigurationJson);
        CreateBinaryFile(System.IO.Path.Combine(configDirectory, "Secrets"), "media-secrets.key", new byte[16]);
        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        ConnectConfigurationLoadResult result = service.Load();

        Assert.False(result.IsSuccess);
        Assert.Contains("Media secret key has an invalid length.", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_InvalidBase64Envelope_ReportsBase64Message()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        Foundry.Core.Models.Configuration.FoundryConnectProvisioningBundle bundle = CreatePersonalWifiProvisioningBundle(tempDirectory.Path);
        Assert.NotNull(bundle.MediaSecretsKey);

        string configDirectory = System.IO.Path.Combine(tempDirectory.Path, "Config");
        Directory.CreateDirectory(configDirectory);
        string configurationJson = ReplacePassphraseSecretProperty(bundle.ConfigurationJson, "ciphertext", "!!");
        string configurationPath = CreateJsonFile(configDirectory, "foundry.connect.config.json", configurationJson);
        CreateBinaryFile(System.IO.Path.Combine(configDirectory, "Secrets"), "media-secrets.key", bundle.MediaSecretsKey);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        ConnectConfigurationLoadResult result = service.Load();
        Assert.False(result.IsSuccess);
        Assert.Null(result.Configuration);
        Assert.Null(result.Secrets);
        string failure = result.FailureMessage!;
        Assert.Contains("invalid base64url data", failure, StringComparison.Ordinal);
    }

    private static FoundryConnectConfigurationDocument LoadConfiguration(ConnectConfigurationService service) =>
        LoadWithSecrets(service).Configuration;

    private static (FoundryConnectConfigurationDocument Configuration, ConnectRuntimeSecrets Secrets) LoadWithSecrets(ConnectConfigurationService service)
    {
        ConnectConfigurationLoadResult result = service.Load();
        Assert.True(result.IsSuccess, result.FailureMessage);
        return (result.Configuration, result.Secrets);
    }

    private sealed class WarningRecordingLogger : ILogger<ConnectConfigurationService>
    {
        public int WarningCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningCount++;
            }
        }
    }

    private static string CreateJsonFile(string directoryPath, string fileName, string contents)
    {
        string filePath = System.IO.Path.Combine(directoryPath, fileName);
        using JsonDocument document = JsonDocument.Parse(contents);
        File.WriteAllText(filePath, document.RootElement.GetRawText());
        return filePath;
    }

    private static CoreConfiguration.AuthoringFileReference Reference(string path)
    {
        byte[] content = File.ReadAllBytes(path);
        return new CoreConfiguration.AuthoringFileReference
        {
            ContentHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content)),
            FileName = System.IO.Path.GetFileName(path),
            Length = content.Length
        };
    }

    // Referenced files are created in the staging directory; the generator reads them through this resolver only.
    private static MediaInputs Inputs(string directory, NetworkSecrets? secrets = null) => new()
    {
        ResolveFile = reference => System.IO.Path.Combine(directory, reference.FileName) is var path && File.Exists(path) ? path : null,
        Network = secrets ?? new NetworkSecrets()
    };

    private static Foundry.Core.Models.Configuration.FoundryConnectProvisioningBundle CreatePersonalWifiProvisioningBundle(string stagingDirectoryPath)
    {
        return new ConnectConfigurationGenerator().CreateProvisioningBundle(
            new CoreConfiguration.FoundryConfigurationDocument
            {
                Network = new CoreConfiguration.NetworkSettings
                {
                    WifiProvisioned = true,
                    Wifi = new CoreConfiguration.WifiSettings
                    {
                        IsEnabled = true,
                        Ssid = "Corp WiFi",
                        SecurityType = "WPA2/WPA3-Personal"
                    }
                }
            },
            Inputs(stagingDirectoryPath, new NetworkSecrets { WifiPassphrase = "super-secret-passphrase" }),
            stagingDirectoryPath);
    }

    private static string ReplacePassphraseSecretProperty(string configurationJson, string propertyName, string value)
    {
        JsonNode root = JsonNode.Parse(configurationJson)!;
        root["wifi"]!["passphraseSecret"]![propertyName] = value;
        return root.ToJsonString();
    }

    private static string CreateBinaryFile(string directoryPath, string fileName, byte[] contents)
    {
        Directory.CreateDirectory(directoryPath);
        string filePath = System.IO.Path.Combine(directoryPath, fileName);
        File.WriteAllBytes(filePath, contents);
        return filePath;
    }

    private static string CreateFile(string directoryPath, string fileName, string contents)
    {
        string filePath = System.IO.Path.Combine(directoryPath, fileName);
        File.WriteAllText(filePath, contents);
        return filePath;
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previousValue);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Foundry.Connect.Tests", Guid.NewGuid().ToString("N"));
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
