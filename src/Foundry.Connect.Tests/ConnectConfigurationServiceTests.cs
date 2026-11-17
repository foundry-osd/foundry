// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Nodes;
using Foundry.Connect.Configuration;
using Foundry.Connect.Tests.Support;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Runtime;
using Foundry.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CoreConfiguration = Foundry.Core.Models.Configuration;

namespace Foundry.Connect.Tests;

public sealed class ConnectConfigurationServiceTests
{
    private static readonly BootMediaRuntimeContext ProductionRuntime = new("26.10.3.2", true, false, false);

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    public void ResolveConfiguration_RequiresTheFileUnderWinPeOrWhenOverridden(bool isOverride, bool isWinPe, bool isRequired)
    {
        var location = new WinPeConfigurationLocation(@"X:\Foundry\Config\foundry.winpe.config.json", isOverride);

        var resolution = ConnectConfigurationService.ResolveConfiguration(location, isWinPe);

        Assert.Equal(location.Path, resolution.Path);
        Assert.Equal(isRequired, resolution.IsRequired);
    }

    [Fact]
    public void Load_WhenRequiredConfigurationIsMissing_IsConfigurationFailure()
    {
        using var directory = new TemporaryDirectory();
        var service = ServiceFor(Path.Combine(directory.Path, "foundry.winpe.config.json"));

        ConnectConfigurationLoadResult result = service.Load();

        Assert.False(result.IsSuccess);
        Assert.IsType<FoundryConnectConfigurationException>(result.FailureException);
        Assert.False(service.IsLoadedFromDisk);
    }

    [Fact]
    public void Load_ReadsTheSharedSectionsAndTheConnectSection()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "foundry.winpe.config.json", """
            {
              "schemaVersion": 1,
              "authoringVersion": "26.11.1.1",
              "telemetry": { "isEnabled": false, "installId": "install-id" },
              "protection": { "isEnabled": false },
              "network": { "profileRoaming": { "wifi": { "isEnabled": true } }, "artifactRootPath": "Y:/Roaming" },
              "connect": {
                "capabilities": { "wifiProvisioned": true },
                "dot1x": { "isEnabled": true, "profileTemplatePath": "Network/Wired/Profiles/wired.xml" },
                "wifi": { "isEnabled": true, "ssid": "Open WiFi", "securityType": "Open" }
              },
              "deploy": { "completion": "not a Connect concern" }
            }
            """);

        FoundryConnectConfigurationDocument configuration = LoadConfiguration(ServiceFor(path));

        Assert.Equal("26.11.1.1", configuration.AuthoringVersion);
        Assert.Equal("install-id", configuration.Telemetry.InstallId);
        Assert.False(configuration.Telemetry.IsEnabled);
        Assert.True(configuration.Network.ProfileRoaming.Wifi.IsEnabled);
        Assert.Equal("Y:/Roaming", configuration.Network.ArtifactRootPath);
        Assert.True(configuration.Capabilities.WifiProvisioned);
        Assert.Equal("Network/Wired/Profiles/wired.xml", configuration.Dot1x.ProfileTemplatePath);
        Assert.Equal("Open WiFi", configuration.Wifi.Ssid);
        Assert.False(configuration.Protection.IsEnabled);
    }

    [Fact]
    public void Load_FindsSectionsWhateverTheirCasing()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "foundry.winpe.config.json",
            """{"SchemaVersion":1,"Telemetry":{"InstallId":"install-id"},"CONNECT":{"Wifi":{"IsEnabled":true,"Ssid":"Lab"}}}""");

        FoundryConnectConfigurationDocument configuration = LoadConfiguration(ServiceFor(path));

        Assert.Equal("install-id", configuration.Telemetry.InstallId);
        Assert.Equal("Lab", configuration.Wifi.Ssid);
    }

    [Fact]
    public void Load_RecommendsRebuildForOlderAuthoringReleaseWithCurrentSchema()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", $$"""{"schemaVersion":{{ConfigurationSchemaVersions.WinPeCurrent}},"authoringVersion":"26.10.3.1"}""");
        var service = new ConnectConfigurationService(path, NullLogger<ConnectConfigurationService>.Instance, ProductionRuntime);

        Assert.True(service.Load().IsSuccess);

        Assert.Equal(BootMediaUpdateReason.NewerRelease, service.BootMediaUpdateReason);
        Assert.True(service.IsBootMediaUpdateRecommended);
    }

    [Fact]
    public void Load_UsesLegacyFallbackWithoutAuthoringMetadata()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", """{"schemaVersion":1}""");
        var service = new ConnectConfigurationService(path, NullLogger<ConnectConfigurationService>.Instance, ProductionRuntime);

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
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", """{"schemaVersion":1,"authoringVersion":"26.10.3.1"}""");
        File.WriteAllText(Path.Combine(directory.Path, WinPeWorkspaceLayout.ProvisioningSourceFileName), source);
        var service = new ConnectConfigurationService(path, NullLogger<ConnectConfigurationService>.Instance, new BootMediaRuntimeContext(version, winPe, debugger, debugBuild));

        Assert.True(service.Load().IsSuccess);

        Assert.Equal(BootMediaUpdateReason.None, service.BootMediaUpdateReason);
        Assert.False(service.IsBootMediaUpdateRecommended);
    }

    [Theory]
    [InlineData("debug", BootMediaUpdateReason.None)]
    [InlineData("unknown", BootMediaUpdateReason.UnknownAuthoringVersion)]
    public void Load_OnlyExplicitDebugTelemetrySuppressesLegacyAdvice(string source, BootMediaUpdateReason expected)
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", $$$"""{"telemetry":{"runtimePayloadSource":"{{{source}}}"}}""");
        var service = new ConnectConfigurationService(path, NullLogger<ConnectConfigurationService>.Instance, ProductionRuntime);

        Assert.True(service.Load().IsSuccess);

        Assert.Equal(expected, service.BootMediaUpdateReason);
    }

    [Theory]
    [InlineData("26.10.3.2")]
    [InlineData("26.10.4.1")]
    public void Load_DoesNotRecommendRebuildForSameOrNewerAuthor(string author)
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", $$"""{"schemaVersion":1,"authoringVersion":"{{author}}"}""");
        var service = new ConnectConfigurationService(path, NullLogger<ConnectConfigurationService>.Instance, ProductionRuntime);

        Assert.True(service.Load().IsSuccess);

        Assert.Equal(BootMediaUpdateReason.None, service.BootMediaUpdateReason);
        Assert.False(service.IsBootMediaUpdateRecommended);
    }

    [Fact]
    public void Load_UsesLegacyFallbackWhenProvisioningMarkerIsUnreadable()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", """{"schemaVersion":1}""");
        Directory.CreateDirectory(Path.Combine(directory.Path, WinPeWorkspaceLayout.ProvisioningSourceFileName));
        var service = new ConnectConfigurationService(path, NullLogger<ConnectConfigurationService>.Instance, ProductionRuntime);

        Assert.True(service.Load().IsSuccess);

        Assert.Equal(BootMediaUpdateReason.UnknownAuthoringVersion, service.BootMediaUpdateReason);
    }

    [Fact]
    public void Load_ClearsPreviousAdviceWhenReloadFails()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", """{"schemaVersion":1}""");
        var service = new ConnectConfigurationService(path, NullLogger<ConnectConfigurationService>.Instance, ProductionRuntime);
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
        var service = ServiceFor("invalid\0path.json");

        ConnectConfigurationLoadResult result = service.Load();

        Assert.False(result.IsSuccess);
        FoundryConnectConfigurationException failure = Assert.IsType<FoundryConnectConfigurationException>(result.FailureException);
        Assert.Equal(failure.Message, result.FailureMessage);
        Assert.IsType<ArgumentException>(failure.InnerException);
    }

    [Fact]
    public void Load_WhenConfigurationFails_LogsTheFailureTypeWithoutTheExceptionOrThePath()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "contoso-site.json");
        var logger = new RecordingLogger<ConnectConfigurationService>();
        var service = ServiceFor(path, logger);

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
    public void CreateDiagnosticFailure_WhenConfigurationFails_KeepsTheFailureKindWithoutThePath()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "contoso-site.json");
        File.WriteAllText(path, "{ not json");
        var service = ServiceFor(path);

        ConnectConfigurationLoadResult result = service.Load();

        Assert.False(result.IsSuccess);
        Assert.Contains("contoso-site", result.FailureException!.Message, StringComparison.Ordinal);
        Exception failure = result.CreateDiagnosticFailure();
        Assert.IsType<FoundryConnectConfigurationException>(failure);
        Assert.Null(failure.InnerException);
        Assert.Contains($"FailureType={result.FailureException.InnerException!.GetType().Name}", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("contoso-site", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WhenConfigurationContainsTelemetry_PreservesTelemetrySettings()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "telemetry.json", """
            {
              "schemaVersion": 1,
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

        FoundryConnectConfigurationDocument configuration = LoadConfiguration(ServiceFor(path));

        Assert.False(configuration.Telemetry.IsEnabled);
        Assert.True(configuration.Telemetry.IsRemoteDiagnosticsEnabled);
        Assert.Equal("install-id", configuration.Telemetry.InstallId);
        Assert.Equal(TelemetryDefaults.PostHogEuHost, configuration.Telemetry.HostUrl);
        Assert.Equal("project-token", configuration.Telemetry.ProjectToken);
        Assert.Equal(TelemetryRuntimePayloadSources.Debug, configuration.Telemetry.RuntimePayloadSource);
    }

    [Fact]
    public void Load_WhenCoreGeneratedConfigurationIsProvided_PreservesEffectiveNetworkPaths()
    {
        using var directory = new TemporaryDirectory();
        string wiredProfilePath = directory.CreateFile("wired.xml", "<LANProfile />");
        string json = ConnectMediaFixtures.Generate(
            new CoreConfiguration.FoundryConfigurationDocument
            {
                Network = new CoreConfiguration.NetworkSettings
                {
                    RoamWiredDot1xProfileToWindows = true,
                    RoamWiredDot1xPrivateKeyMaterialToWindows = true,
                    Dot1x = new CoreConfiguration.Dot1xSettings { IsEnabled = true, ProfileTemplate = Reference(wiredProfilePath) }
                }
            },
            Inputs(directory.Path),
            directory.Path);
        string path = CreateJsonFile(directory.Path, "foundry.winpe.config.json", json);

        FoundryConnectConfigurationDocument configuration = LoadConfiguration(ServiceFor(path));

        Assert.Equal(ConfigurationSchemaVersions.WinPeCurrent, configuration.SchemaVersion);
        Assert.Equal(Path.Combine("Network", "Wired", "Profiles", "wired.xml"), configuration.Dot1x.ProfileTemplatePath);
        Assert.True(configuration.Network.ProfileRoaming.WiredDot1x.IncludePrivateKeyMaterial);
        Assert.False(configuration.Network.ProfileRoaming.Wifi.IsEnabled);
        Assert.False(configuration.Protection.IsEnabled);
    }

    [Fact]
    public void Load_SchemaZero_IsConfigurationFailure()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", """{"schemaVersion":0}""");
        var service = ServiceFor(path);

        ConnectConfigurationLoadResult result = service.Load();

        Assert.False(result.IsSuccess);
        Assert.Null(result.Configuration);
        Assert.Null(result.Secrets);
        Assert.Contains("schema version 0", result.FailureMessage, StringComparison.Ordinal);
        Assert.False(service.IsLoadedFromDisk);
    }

    [Fact]
    public void Load_NullSections_UseDefaults()
    {
        using var directory = new TemporaryDirectory();
        string path = CreateJsonFile(directory.Path, "config.json", """{"schemaVersion":1,"telemetry":null,"protection":null,"connect":null}""");

        FoundryConnectConfigurationDocument configuration = LoadConfiguration(ServiceFor(path));

        Assert.Equal(new TelemetrySettings(), configuration.Telemetry);
        Assert.False(configuration.Protection.IsEnabled);
        Assert.False(configuration.Wifi.IsEnabled);
    }

    [Fact]
    public void Load_NewerSchema_LoadsAndLogsWarning()
    {
        using var directory = new TemporaryDirectory();
        int newerVersion = ConfigurationSchemaVersions.WinPeCurrent + 1;
        string path = CreateJsonFile(directory.Path, "config.json", $$"""{"schemaVersion":{{newerVersion}}}""");
        var logger = new RecordingLogger<ConnectConfigurationService>();
        var service = ServiceFor(path, logger);

        FoundryConnectConfigurationDocument configuration = LoadConfiguration(service);

        Assert.Equal(newerVersion, configuration.SchemaVersion);
        Assert.True(service.IsLoadedFromDisk);
        Assert.Single(logger.Entries, static entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void Load_ProtectedMedia_IsLockedUntilUnlocked()
    {
        using var directory = new TemporaryDirectory();
        (string path, byte[] key) = ConnectMediaFixtures.WriteProtected(directory, PersonalWifiAndWiredPfxAuthoring(directory),
            new NetworkSecrets { WifiPassphrase = "lab-passphrase", WiredCertificatePfxPassword = "wired-password" });
        var logger = new RecordingLogger<ConnectConfigurationService>();

        ConnectConfigurationLoadResult result = ServiceFor(path, logger).Load();

        Assert.True(result.IsSuccess, result.FailureMessage);
        ConnectRuntimeSecrets secrets = result.Secrets!;
        Assert.Equal(ConnectSecretStatus.Locked, secrets.WifiPassphrase.Status);
        Assert.Equal(ConnectSecretStatus.Locked, secrets.WiredCertificatePfxPassword.Status);
        Assert.Equal(ConnectSecretStatus.Locked, secrets.WiredClientCertificate.Status);
        Assert.NotNull(secrets.Protection);
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
        using var directory = new TemporaryDirectory();
        (string path, byte[] key) = ConnectMediaFixtures.WriteProtected(directory, EnterpriseWifiAuthoring(directory),
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

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Load_MediaThatCannotBeUnlocked_MakesDeploymentKeyedSecretsUnavailable(bool isEnabled, bool hasProtectedKey)
    {
        using var directory = new TemporaryDirectory();
        (string path, _) = ConnectMediaFixtures.WriteProtected(directory, PersonalWifiAndWiredPfxAuthoring(directory),
            new NetworkSecrets { WifiPassphrase = "lab-passphrase", WiredCertificatePfxPassword = "wired-password" });
        JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        JsonObject protection = root["protection"]!.AsObject();
        protection["isEnabled"] = isEnabled;
        if (!hasProtectedKey)
        {
            protection.Remove("protectedDeploymentKey");
        }

        File.WriteAllText(path, root.ToJsonString());

        ConnectRuntimeSecrets secrets = ServiceFor(path).Load().Secrets!;

        Assert.Equal(ConnectSecretStatus.Unavailable, secrets.WifiPassphrase.Status);
        Assert.Equal(ConnectSecretFailure.DecryptionFailed, secrets.WifiPassphrase.Failure);
        Assert.Equal(ConnectSecretStatus.Unavailable, secrets.WiredClientCertificate.Status);
        Assert.False(secrets.IsWifiLocked);
        Assert.False(secrets.IsWiredLocked);
        Assert.Null(secrets.Protection);
    }

    [Fact]
    public void Load_EnvelopeNotKeyedToTheDeploymentKey_IsUnavailable()
    {
        using var directory = new TemporaryDirectory();
        (string path, _) = ConnectMediaFixtures.WriteProtected(directory, PersonalWifiAndWiredPfxAuthoring(directory),
            new NetworkSecrets { WifiPassphrase = "lab-passphrase", WiredCertificatePfxPassword = "wired-password" });
        ReplacePassphraseSecretProperty(path, "keyId", "media");

        ConnectRuntimeSecrets secrets = ServiceFor(path).Load().Secrets!;

        Assert.Equal(ConnectSecretStatus.Unavailable, secrets.WifiPassphrase.Status);
        Assert.Equal(ConnectSecretStatus.Locked, secrets.WiredCertificatePfxPassword.Status);
    }

    [Fact]
    public void Load_ProtectedClientCertificateFileMissing_IsUnavailableWithoutNamingTheFile()
    {
        using var directory = new TemporaryDirectory();
        (string path, _) = ConnectMediaFixtures.WriteProtected(directory, PersonalWifiAndWiredPfxAuthoring(directory),
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
        using var directory = new TemporaryDirectory();
        (string path, _) = ConnectMediaFixtures.WriteProtected(directory, PersonalWifiAndWiredPfxAuthoring(directory),
            new NetworkSecrets { WifiPassphrase = "lab-passphrase", WiredCertificatePfxPassword = "wired-password" });
        File.WriteAllText(ProtectedWiredCertificatePath(path), content);

        ConnectRuntimeSecrets secrets = ServiceFor(path).Load().Secrets!;

        Assert.Equal(ConnectSecretStatus.Unavailable, secrets.WiredClientCertificate.Status);
        Assert.Equal(ConnectSecretFailure.DecryptionFailed, secrets.WiredClientCertificate.Failure);
    }

    [Fact]
    public void Load_ProtectedClientCertificateFileOverTheSharedLimit_IsUnavailable()
    {
        using var directory = new TemporaryDirectory();
        (string path, _) = ConnectMediaFixtures.WriteProtected(directory, PersonalWifiAndWiredPfxAuthoring(directory),
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
        using var directory = new TemporaryDirectory();
        (string path, byte[] key) = ConnectMediaFixtures.WriteProtected(directory, PersonalWifiAndWiredPfxAuthoring(directory),
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
        new(configurationPath, logger ?? NullLogger<ConnectConfigurationService>.Instance);

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

    private static void ReplacePassphraseSecretProperty(string configurationPath, string propertyName, string value)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(configurationPath))!;
        root["connect"]!["wifi"]!["passphraseSecret"]![propertyName] = value;
        File.WriteAllText(configurationPath, root.ToJsonString());
    }
}
