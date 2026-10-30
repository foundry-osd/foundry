// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Nodes;
using Foundry.Connect.Configuration;
using Foundry.Connect.Tests.Support;
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
    public void Load_WhenConfigurationPathIsInvalid_ReportsConfigurationFailureWithOriginalException()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        var service = new ConnectConfigurationService(["--config", "invalid\0path.json"], NullLogger<ConnectConfigurationService>.Instance);

        ConnectConfigurationLoadResult result = service.Load();

        Assert.False(result.IsSuccess);
        FoundryConnectConfigurationException failure = Assert.IsType<FoundryConnectConfigurationException>(result.FailureException);
        Assert.Equal(failure.Message, result.FailureMessage);
        Assert.IsType<ArgumentException>(failure.InnerException);
    }

    [Fact]
    public void Load_WhenConfigurationFails_LogsTheFailureTypeWithoutTheExceptionOrThePath()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "contoso-site.json");
        var logger = new RecordingLogger<ConnectConfigurationService>();
        var service = new ConnectConfigurationService(["--config", path], logger);

        ConnectConfigurationLoadResult result = service.Load();

        Assert.False(result.IsSuccess);
        RecordedLogEntry entry = Assert.Single(logger.Entries, static entry => entry.Message.Contains("could not be loaded", StringComparison.Ordinal));
        Assert.Null(entry.Exception);
        Assert.Equal(nameof(FoundryConnectConfigurationException), entry.Properties["FailureType"]);
        Assert.True(entry.Properties.ContainsKey("ErrorCode"));
        Assert.DoesNotContain("contoso-site", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(entry.Properties.Values, static value => value?.ToString()?.Contains("contoso-site", StringComparison.Ordinal) == true);
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
    public void Load_WhenSchemaFiveConfigurationContainsEncryptedPassphrase_DecryptsPassphraseFromMediaKey()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = ConnectMediaFixtures.WriteSchemaFive(tempDirectory, wifiPassphrase: "super-secret-passphrase");
        Assert.DoesNotContain("super-secret-passphrase", File.ReadAllText(configurationPath), StringComparison.Ordinal);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        (FoundryConnectConfigurationDocument configuration, ConnectRuntimeSecrets secrets) = LoadWithSecrets(service);

        Assert.Equal(5, configuration.SchemaVersion);
        Assert.Equal("super-secret-passphrase", new string(secrets.WifiPassphrase.Value));
        Assert.Null(configuration.Wifi.PassphraseSecret);
    }

    [Fact]
    public void Load_WhenSchemaFiveConfigurationContainsEncryptedPfxPasswords_DecryptsPasswordsFromMediaKey()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = ConnectMediaFixtures.WriteSchemaFive(
            tempDirectory,
            wiredCertificatePath: @"Network\Certificates\Wired\wired.pfx",
            wiredPfxPassword: "wired-password",
            wifiCertificatePath: @"Network\Certificates\Wifi\wifi.pfx",
            wifiPfxPassword: "wifi-password");
        string configurationJson = File.ReadAllText(configurationPath);
        Assert.DoesNotContain("wired-password", configurationJson, StringComparison.Ordinal);
        Assert.DoesNotContain("wifi-password", configurationJson, StringComparison.Ordinal);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        (FoundryConnectConfigurationDocument configuration, ConnectRuntimeSecrets secrets) = LoadWithSecrets(service);

        Assert.Equal("wired-password", new string(secrets.WiredCertificatePfxPassword.Value));
        Assert.Equal("wifi-password", new string(secrets.WifiCertificatePfxPassword.Value));
        Assert.NotNull(configuration.Dot1x.CertificatePfxPasswordSecret);
        Assert.NotNull(configuration.Wifi.CertificatePfxPasswordSecret);
    }

    [Fact]
    public void Load_SchemaFiveDeadRuntimeFields_StillLoad()
    {
        // Schema 5 media carry the fields schema 6 no longer writes; Connect still reads them.
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = ConnectMediaFixtures.WriteSchemaFive(
            tempDirectory,
            wiredCertificatePath: @"Network\Certificates\Wired\wired.pfx",
            wiredPfxPassword: "wired-password",
            wifiCertificatePath: @"Network\Certificates\Wifi\wifi.pfx",
            wifiPfxPassword: "wifi-password");
        string configurationJson = File.ReadAllText(configurationPath);
        Assert.All(
            ["\"allowRuntimeCredentials\"", "\"authenticationMode\"", "\"enterpriseAuthenticationMode\"", "\"requiresCertificate\"", "\"internetProbe\""],
            name => Assert.Contains(name, configurationJson, StringComparison.Ordinal));

        FoundryConnectConfigurationDocument configuration = LoadConfiguration(
            new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance));

        Assert.Equal(CoreConfiguration.NetworkAuthenticationMode.MachineOnly, configuration.Dot1x.AuthenticationMode);
        Assert.Equal(CoreConfiguration.NetworkAuthenticationMode.UserOnly, configuration.Wifi.EnterpriseAuthenticationMode);
        Assert.True(configuration.Dot1x.RequiresCertificate);
    }

    [Fact]
    public void Load_WhenEncryptedPassphraseHasNoMediaKey_MarksPassphraseUnavailable()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = ConnectMediaFixtures.WriteSchemaFive(tempDirectory, wifiPassphrase: "super-secret-passphrase");
        File.Delete(MediaKeyPath(configurationPath));

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        ConnectConfigurationLoadResult result = service.Load();
        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Equal(ConnectSecretStatus.Unavailable, result.Secrets!.WifiPassphrase.Status);
        Assert.Equal(ConnectSecretFailure.MediaKeyMissing, result.Secrets.WifiPassphrase.Failure);
    }

    [Fact]
    public void Load_WhenEncryptedPassphraseUsesUnsupportedEnvelope_MarksPassphraseUnavailable()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = ConnectMediaFixtures.WriteSchemaFive(tempDirectory, wifiPassphrase: "super-secret-passphrase");
        ReplacePassphraseSecretProperty(configurationPath, "algorithm", "unsupported");

        var logger = new RecordingLogger<ConnectConfigurationService>();
        var service = new ConnectConfigurationService(["--config", configurationPath], logger);

        ConnectConfigurationLoadResult result = service.Load();
        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Equal(ConnectSecretStatus.Unavailable, result.Secrets!.WifiPassphrase.Status);
        Assert.Equal(ConnectSecretFailure.DecryptionFailed, result.Secrets.WifiPassphrase.Failure);
        Assert.All(logger.Entries, static entry => Assert.DoesNotContain("super-secret-passphrase", entry.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void Load_WhenEncryptedPassphraseIsTampered_MarksPassphraseUnavailable()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = ConnectMediaFixtures.WriteSchemaFive(tempDirectory, wifiPassphrase: "super-secret-passphrase");
        ReplacePassphraseSecretProperty(configurationPath, "ciphertext", "AA");

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        ConnectConfigurationLoadResult result = service.Load();
        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Equal(ConnectSecretStatus.Unavailable, result.Secrets!.WifiPassphrase.Status);
        Assert.Equal(ConnectSecretFailure.DecryptionFailed, result.Secrets.WifiPassphrase.Failure);
    }

    [Fact]
    public void Load_WhenOnlyWifiPassphraseIsTampered_KeepsWiredPasswordAvailable()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = WritePersonalWifiAndWiredSchemaFive(tempDirectory);
        ReplacePassphraseSecretProperty(configurationPath, "ciphertext", "AA");

        var logger = new RecordingLogger<ConnectConfigurationService>();
        var service = new ConnectConfigurationService(["--config", configurationPath], logger);

        (_, ConnectRuntimeSecrets secrets) = LoadWithSecrets(service);

        Assert.Equal(ConnectSecretStatus.Unavailable, secrets.WifiPassphrase.Status);
        Assert.Equal(ConnectSecretFailure.DecryptionFailed, secrets.WifiPassphrase.Failure);
        Assert.Equal(ConnectSecretStatus.Available, secrets.WiredCertificatePfxPassword.Status);
        Assert.Equal("wired-password", new string(secrets.WiredCertificatePfxPassword.Value));
        RecordedLogEntry warning = Assert.Single(logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Equal("Provisioned secret is unavailable. Secret=wifi_passphrase, Failure=DecryptionFailed", warning.Message);
        Assert.Equal("wifi_passphrase", warning.Properties["SecretName"]);
        Assert.Equal(ConnectSecretFailure.DecryptionFailed, warning.Properties["SecretFailure"]);
    }

    [Fact]
    public void Load_WhenMediaKeyCannotBeRead_MarksDependentSecretsUnavailable()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = WritePersonalWifiAndWiredSchemaFive(tempDirectory);
        string keyPath = MediaKeyPath(configurationPath);
        var logger = new RecordingLogger<ConnectConfigurationService>();
        var service = new ConnectConfigurationService(["--config", configurationPath], logger);

        ConnectRuntimeSecrets secrets;
        // An exclusive handle makes File.ReadAllBytes fail with a sharing violation (IOException) while the file still exists.
        using (new FileStream(keyPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            (_, secrets) = LoadWithSecrets(service);
        }

        Assert.Equal(ConnectSecretStatus.Unavailable, secrets.WifiPassphrase.Status);
        Assert.Equal(ConnectSecretFailure.MediaKeyUnreadable, secrets.WifiPassphrase.Failure);
        Assert.Equal(ConnectSecretStatus.Unavailable, secrets.WiredCertificatePfxPassword.Status);
        Assert.Equal(ConnectSecretFailure.MediaKeyUnreadable, secrets.WiredCertificatePfxPassword.Failure);
        Assert.Equal(ConnectSecretStatus.NotProvisioned, secrets.WifiCertificatePfxPassword.Status);
        RecordedLogEntry[] warnings = [.. logger.Entries.Where(static entry => entry.Level == LogLevel.Warning)];
        Assert.Equal(["wifi_passphrase", "wired_certificate_password"], warnings.Select(static entry => entry.Properties["SecretName"]));
        Assert.All(warnings, static entry => Assert.Equal(ConnectSecretFailure.MediaKeyUnreadable, entry.Properties["SecretFailure"]));
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

        Assert.Equal("legacy-passphrase", new string(secrets.WifiPassphrase.Value));
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
    public void Load_WhenMediaSecretKeyHasInvalidLength_MarksPassphraseUnavailable()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = ConnectMediaFixtures.WriteSchemaFive(tempDirectory, wifiPassphrase: "super-secret-passphrase");
        File.WriteAllBytes(MediaKeyPath(configurationPath), new byte[16]);
        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        ConnectConfigurationLoadResult result = service.Load();

        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Equal(ConnectSecretStatus.Unavailable, result.Secrets!.WifiPassphrase.Status);
        Assert.Equal(ConnectSecretFailure.MediaKeyInvalid, result.Secrets.WifiPassphrase.Failure);
    }

    [Fact]
    public void Load_InvalidBase64Envelope_MarksPassphraseUnavailable()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = ConnectMediaFixtures.WriteSchemaFive(tempDirectory, wifiPassphrase: "super-secret-passphrase");
        ReplacePassphraseSecretProperty(configurationPath, "ciphertext", "!!");

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        ConnectConfigurationLoadResult result = service.Load();
        Assert.True(result.IsSuccess, result.FailureMessage);
        Assert.Equal(ConnectSecretStatus.Unavailable, result.Secrets!.WifiPassphrase.Status);
        Assert.Equal(ConnectSecretFailure.DecryptionFailed, result.Secrets.WifiPassphrase.Failure);
    }

    [Fact]
    public void Load_ProtectedSchemaSix_IsLockedUntilUnlocked()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var directory = new TemporaryDirectory();
        (string path, byte[] key) = ConnectMediaFixtures.WriteProtectedSchemaSix(directory, PersonalWifiAndWiredPfxAuthoring(directory),
            new NetworkSecrets { WifiPassphrase = "lab-passphrase", WiredCertificatePfxPassword = "wired-password" });
        var logger = new RecordingLogger<ConnectConfigurationService>();

        ConnectConfigurationLoadResult result = ServiceFor(path, logger).Load();

        Assert.True(result.IsSuccess, result.FailureMessage);
        ConnectRuntimeSecrets secrets = result.Secrets!;
        Assert.Equal(ConnectSecretStatus.Locked, secrets.WifiPassphrase.Status);
        Assert.Equal(ConnectSecretStatus.Locked, secrets.WiredCertificatePfxPassword.Status);
        Assert.Equal(ConnectSecretStatus.Locked, secrets.WiredClientCertificate.Status);
        Assert.NotNull(secrets.Protection);
        Assert.False(File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "Secrets", "media-secrets.key")));
        Assert.DoesNotContain(logger.Entries, static entry => entry.Level >= LogLevel.Warning);
        RecordedLogEntry locked = Assert.Single(logger.Entries, static entry => entry.Message.StartsWith("Protected network settings found.", StringComparison.Ordinal));
        Assert.Equal("Protected network settings found. Wired=True, Wifi=True", locked.Message);
        Assert.Equal(3, secrets.Unlock(key));
        Assert.Equal("lab-passphrase", new string(secrets.WifiPassphrase.Value));
        Assert.Equal("wired-password", new string(secrets.WiredCertificatePfxPassword.Value));
        Assert.Equal("wired-pfx"u8.ToArray(), secrets.WiredClientCertificate.Content.ToArray());
    }

    [Fact]
    public void Load_ProtectedEnterpriseWifi_LocksTheCertificateAndTheKeyedTemplate()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var directory = new TemporaryDirectory();
        (string path, byte[] key) = ConnectMediaFixtures.WriteProtectedSchemaSix(directory, EnterpriseWifiAuthoring(directory),
            new NetworkSecrets { WifiCertificatePfxPassword = "wifi-password" });

        ConnectRuntimeSecrets secrets = ServiceFor(path).Load().Secrets!;

        Assert.False(secrets.IsWiredLocked);
        Assert.True(secrets.IsWifiLocked);
        Assert.Equal(ConnectSecretStatus.Locked, secrets.WifiCertificatePfxPassword.Status);
        Assert.Equal(ConnectSecretStatus.Locked, secrets.WifiClientCertificate.Status);
        Assert.Equal(ConnectSecretStatus.Locked, secrets.WifiEnterpriseProfile.Status);
        Assert.Equal(3, secrets.Unlock(key));
        Assert.Equal(KeyedEnterpriseTemplate, System.Text.Encoding.UTF8.GetString(secrets.WifiEnterpriseProfile.Content));
        secrets.ClearAll();
        Assert.Equal(ConnectSecretStatus.Cleared, secrets.WifiEnterpriseProfile.Status);
        Assert.Equal(ConnectSecretStatus.Cleared, secrets.WifiClientCertificate.Status);
    }

    [Fact]
    public void Load_SchemaFiveMediaKeyedSecrets_AreAvailableWithoutUnlock()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var directory = new TemporaryDirectory();
        string path = ConnectMediaFixtures.WriteSchemaFive(directory, wifiPassphrase: "lab-passphrase",
            wiredCertificatePath: @"Network\Certificates\Wired\wired.pfx", wiredPfxPassword: "wired-password");

        ConnectRuntimeSecrets secrets = ServiceFor(path).Load().Secrets!;

        Assert.Equal("lab-passphrase", new string(secrets.WifiPassphrase.Value));
        Assert.Equal("wired-password", new string(secrets.WiredCertificatePfxPassword.Value));
        Assert.False(secrets.IsWiredLocked || secrets.IsWifiLocked);
        Assert.Equal(ConnectSecretStatus.NotProvisioned, secrets.WiredClientCertificate.Status);
    }

    [Fact]
    public void Load_DeploymentKeyedEnvelopeWithoutProtectionBlock_IsUnavailable()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var directory = new TemporaryDirectory();
        (string path, _) = ConnectMediaFixtures.WriteProtectedSchemaSix(directory, PersonalWifiAndWiredPfxAuthoring(directory),
            new NetworkSecrets { WifiPassphrase = "lab-passphrase", WiredCertificatePfxPassword = "wired-password" });
        JsonNode root = JsonNode.Parse(File.ReadAllText(path))!;
        root.AsObject().Remove("protection");
        File.WriteAllText(path, root.ToJsonString());

        ConnectRuntimeSecrets secrets = ServiceFor(path).Load().Secrets!;

        Assert.Equal(ConnectSecretStatus.Unavailable, secrets.WifiPassphrase.Status);
        Assert.Equal(ConnectSecretFailure.DecryptionFailed, secrets.WifiPassphrase.Failure);
        Assert.Equal(ConnectSecretStatus.Unavailable, secrets.WiredClientCertificate.Status);
        Assert.False(secrets.IsWifiLocked);
        Assert.False(secrets.IsWiredLocked);
    }

    [Fact]
    public void Load_ProtectedClientCertificateFileMissing_IsUnavailableWithoutNamingTheFile()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var directory = new TemporaryDirectory();
        (string path, _) = ConnectMediaFixtures.WriteProtectedSchemaSix(directory, PersonalWifiAndWiredPfxAuthoring(directory),
            new NetworkSecrets { WifiPassphrase = "lab-passphrase", WiredCertificatePfxPassword = "wired-password" });
        File.Delete(ProtectedWiredCertificatePath(path));
        var logger = new RecordingLogger<ConnectConfigurationService>();

        ConnectRuntimeSecrets secrets = ServiceFor(path, logger).Load().Secrets!;

        Assert.Equal(ConnectSecretStatus.Unavailable, secrets.WiredClientCertificate.Status);
        Assert.Equal(ConnectSecretFailure.DecryptionFailed, secrets.WiredClientCertificate.Failure);
        Assert.Equal(ConnectSecretStatus.Locked, secrets.WiredCertificatePfxPassword.Status);
        RecordedLogEntry warning = Assert.Single(logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Equal("Provisioned secret is unavailable. Secret=wired_client_certificate, Failure=DecryptionFailed", warning.Message);
        Assert.All(logger.Entries, static entry => Assert.DoesNotContain("client.pfx", entry.Message, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""{"kind":"encrypted","algorithm":"aes-gcm-v1","keyId":"media","nonce":"AA","tag":"AA","ciphertext":"AA"}""")]
    public void Load_ProtectedClientCertificateFileNotADeploymentEnvelope_IsUnavailable(string content)
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var directory = new TemporaryDirectory();
        (string path, _) = ConnectMediaFixtures.WriteProtectedSchemaSix(directory, PersonalWifiAndWiredPfxAuthoring(directory),
            new NetworkSecrets { WifiPassphrase = "lab-passphrase", WiredCertificatePfxPassword = "wired-password" });
        File.WriteAllText(ProtectedWiredCertificatePath(path), content);

        ConnectRuntimeSecrets secrets = ServiceFor(path).Load().Secrets!;

        Assert.Equal(ConnectSecretStatus.Unavailable, secrets.WiredClientCertificate.Status);
        Assert.Equal(ConnectSecretFailure.DecryptionFailed, secrets.WiredClientCertificate.Failure);
    }

    [Fact]
    public void Load_ProtectedClientCertificateFileOverTheSharedLimit_IsUnavailable()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var directory = new TemporaryDirectory();
        (string path, _) = ConnectMediaFixtures.WriteProtectedSchemaSix(directory, PersonalWifiAndWiredPfxAuthoring(directory),
            new NetworkSecrets { WifiPassphrase = "lab-passphrase", WiredCertificatePfxPassword = "wired-password" });
        // Trailing whitespace keeps the envelope valid JSON, so only the size limit rejects it.
        string certificatePath = ProtectedWiredCertificatePath(path);
        File.AppendAllText(certificatePath, new string(' ', ConnectProtectedFileLimits.MaxEnvelopeFileBytes + 1 - (int)new FileInfo(certificatePath).Length));

        ConnectRuntimeSecrets secrets = ServiceFor(path).Load().Secrets!;

        Assert.Equal(ConnectSecretStatus.Unavailable, secrets.WiredClientCertificate.Status);
    }

    [Fact]
    public void Load_ProtectedClientCertificateFileAtTheSharedLimit_IsLocked()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var directory = new TemporaryDirectory();
        (string path, byte[] key) = ConnectMediaFixtures.WriteProtectedSchemaSix(directory, PersonalWifiAndWiredPfxAuthoring(directory),
            new NetworkSecrets { WifiPassphrase = "lab-passphrase", WiredCertificatePfxPassword = "wired-password" });
        string certificatePath = ProtectedWiredCertificatePath(path);
        File.AppendAllText(certificatePath, new string(' ', ConnectProtectedFileLimits.MaxEnvelopeFileBytes - (int)new FileInfo(certificatePath).Length));

        ConnectRuntimeSecrets secrets = ServiceFor(path).Load().Secrets!;

        Assert.Equal(ConnectSecretStatus.Locked, secrets.WiredClientCertificate.Status);
        Assert.Equal(3, secrets.Unlock(key));
    }

    private static FoundryConnectConfigurationDocument LoadConfiguration(ConnectConfigurationService service) =>
        LoadWithSecrets(service).Configuration;

    private static (FoundryConnectConfigurationDocument Configuration, ConnectRuntimeSecrets Secrets) LoadWithSecrets(ConnectConfigurationService service)
    {
        ConnectConfigurationLoadResult result = service.Load();
        Assert.True(result.IsSuccess, result.FailureMessage);
        return (result.Configuration, result.Secrets);
    }

    private static ConnectConfigurationService ServiceFor(string configurationPath, ILogger<ConnectConfigurationService>? logger = null) =>
        new(["--config", configurationPath], logger ?? NullLogger<ConnectConfigurationService>.Instance);

    private const string KeyedEnterpriseTemplate =
        """<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1"><name>Corp</name><MSM><security><sharedKey><keyMaterial>secret</keyMaterial></sharedKey></security></MSM></WLANProfile>""";

    // Password protection on, wired 802.1X with a client PFX and a trusted root, and personal Wi-Fi "Lab"; the files live directly under the directory.
    private static CoreConfiguration.FoundryConfigurationDocument PersonalWifiAndWiredPfxAuthoring(TemporaryDirectory directory) => new()
    {
        General = new CoreConfiguration.GeneralSettings { DeploymentProtection = new CoreConfiguration.DeploymentProtectionSettings { IsEnabled = true } },
        Network = new CoreConfiguration.NetworkSettings
        {
            WifiProvisioned = true,
            Dot1x = new CoreConfiguration.Dot1xSettings
            {
                IsEnabled = true,
                ProfileTemplate = Reference(directory.CreateFile("wired.xml", "<LANProfile />")),
                ClientCertificate = Reference(directory.CreateFile("client.pfx", "wired-pfx")),
                TrustedRootCertificates = [Reference(directory.CreateFile("root.cer", "root"))]
            },
            Wifi = new CoreConfiguration.WifiSettings
            {
                IsEnabled = true,
                Ssid = "Lab",
                SecurityType = NetworkConfigurationValidator.WifiSecurityPersonal
            }
        }
    };

    // Password protection on and enterprise Wi-Fi with a template carrying key material and a client PFX.
    private static CoreConfiguration.FoundryConfigurationDocument EnterpriseWifiAuthoring(TemporaryDirectory directory) => new()
    {
        General = new CoreConfiguration.GeneralSettings { DeploymentProtection = new CoreConfiguration.DeploymentProtectionSettings { IsEnabled = true } },
        Network = new CoreConfiguration.NetworkSettings
        {
            WifiProvisioned = true,
            Wifi = new CoreConfiguration.WifiSettings
            {
                IsEnabled = true,
                Ssid = "Corp",
                SecurityType = NetworkConfigurationValidator.WifiSecurityEnterprise,
                HasEnterpriseProfile = true,
                EnterpriseProfileTemplate = Reference(directory.CreateFile("wifi.xml", KeyedEnterpriseTemplate)),
                ClientCertificate = Reference(directory.CreateFile("wifi.pfx", "wifi-pfx"))
            }
        }
    };

    private static string ProtectedWiredCertificatePath(string configurationPath) =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(configurationPath)!, @"Network\Certificates\Wired\client.pfx.encrypted");

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
    private static MediaInputs Inputs(string directory) => new()
    {
        ResolveFile = reference => System.IO.Path.Combine(directory, reference.FileName) is var path && File.Exists(path) ? path : null
    };

    private static string WritePersonalWifiAndWiredSchemaFive(TemporaryDirectory directory) =>
        ConnectMediaFixtures.WriteSchemaFive(
            directory,
            wifiPassphrase: "super-secret-passphrase",
            wiredCertificatePath: @"Network\Certificates\Wired\wired.pfx",
            wiredPfxPassword: "wired-password");

    private static string MediaKeyPath(string configurationPath) =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(configurationPath)!, WinPeWorkspaceLayout.MediaSecretsKeyRelativePath);

    private static void ReplacePassphraseSecretProperty(string configurationPath, string propertyName, string value)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(configurationPath))!;
        root["wifi"]!["passphraseSecret"]![propertyName] = value;
        File.WriteAllText(configurationPath, root.ToJsonString());
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
}
