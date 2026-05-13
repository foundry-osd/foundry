using System.Text.Json;
using System.Text.Json.Nodes;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Services.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using CoreConfiguration = Foundry.Core.Models.Configuration;

namespace Foundry.Connect.Tests;

public sealed class ConnectConfigurationServiceTests
{
    [Fact]
    public void Load_WhenNoConfigurationFileIsAvailable_ReturnsNormalizedDefaults()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        var service = new ConnectConfigurationService([], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfiguration configuration = service.Load();

        Assert.False(service.IsLoadedFromDisk);
        Assert.Null(service.ConfigurationPath);
        Assert.Equal(FoundryConnectConfiguration.CurrentSchemaVersion, configuration.SchemaVersion);
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
              "schemaVersion": 0,
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

        FoundryConnectConfiguration configuration = service.Load();

        Assert.True(service.IsLoadedFromDisk);
        Assert.Equal(System.IO.Path.GetFullPath(configurationPath), service.ConfigurationPath);
        Assert.Equal(FoundryConnectConfiguration.CurrentSchemaVersion, configuration.SchemaVersion);
        Assert.Equal(30, configuration.InternetProbe.TimeoutSeconds);
        Assert.Equal(
            ["https://example.com/health", "http://contoso.test/connect"],
            configuration.InternetProbe.ProbeUris);
    }

    [Fact]
    public void Load_WhenConfigurationContainsTelemetry_PreservesTelemetrySettings()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        string configurationPath = CreateJsonFile(
            tempDirectory.Path,
            "telemetry.json",
            """
            {
              "schemaVersion": 1,
              "telemetry": {
                "isEnabled": false,
                "installId": "install-id",
                "hostUrl": "https://eu.i.posthog.com",
                "projectToken": "project-token",
                "runtimePayloadSource": "debug"
              }
            }
            """);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfiguration configuration = service.Load();

        Assert.False(configuration.Telemetry.IsEnabled);
        Assert.Equal("install-id", configuration.Telemetry.InstallId);
        Assert.Equal(TelemetryDefaults.PostHogEuHost, configuration.Telemetry.HostUrl);
        Assert.Equal("project-token", configuration.Telemetry.ProjectToken);
        Assert.Equal(TelemetryRuntimePayloadSources.Debug, configuration.Telemetry.RuntimePayloadSource);
    }

    [Fact]
    public void Load_WhenEnvironmentVariableIsSet_TakesPrecedenceOverCommandLineArgument()
    {
        using var tempDirectory = new TemporaryDirectory();
        string environmentConfigurationPath = CreateJsonFile(tempDirectory.Path, "environment.json", """{ "schemaVersion": 5 }""");
        string commandLineConfigurationPath = CreateJsonFile(tempDirectory.Path, "argument.json", """{ "schemaVersion": 2 }""");
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", environmentConfigurationPath);

        var service = new ConnectConfigurationService(["--config", commandLineConfigurationPath], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfiguration configuration = service.Load();

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
            new CoreConfiguration.FoundryExpertConfigurationDocument
            {
                Network = new CoreConfiguration.NetworkSettings
                {
                    Dot1x = new CoreConfiguration.Dot1xSettings
                    {
                        IsEnabled = true,
                        ProfileTemplatePath = wiredProfilePath
                    }
                }
            },
            tempDirectory.Path).ConfigurationJson;
        string configurationPath = CreateJsonFile(tempDirectory.Path, "core-generated.json", configurationJson);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfiguration configuration = service.Load();

        Assert.True(service.IsLoadedFromDisk);
        Assert.Equal(CoreConfiguration.FoundryConnectConfigurationDocument.CurrentSchemaVersion, configuration.SchemaVersion);
        Assert.Equal(@"Network\Wired\Profiles\wired.xml", configuration.Dot1x.ProfileTemplatePath);
        Assert.NotNull(configuration.Capabilities);
        Assert.NotNull(configuration.Wifi);
        Assert.NotNull(configuration.InternetProbe);
    }

    [Fact]
    public void Load_WhenCoreGeneratedConfigurationContainsEncryptedPassphrase_DecryptsPassphraseFromMediaKey()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        Foundry.Core.Models.Configuration.FoundryConnectProvisioningBundle bundle =
            new ConnectConfigurationGenerator().CreateProvisioningBundle(
                new CoreConfiguration.FoundryExpertConfigurationDocument
                {
                    Network = new CoreConfiguration.NetworkSettings
                    {
                        WifiProvisioned = true,
                        Wifi = new CoreConfiguration.WifiSettings
                        {
                            IsEnabled = true,
                            Ssid = "Corp WiFi",
                            SecurityType = "WPA2/WPA3-Personal",
                            Passphrase = "super-secret-passphrase"
                        }
                    }
                },
                tempDirectory.Path);
        Assert.NotNull(bundle.MediaSecretsKey);
        Assert.DoesNotContain("super-secret-passphrase", bundle.ConfigurationJson, StringComparison.Ordinal);

        string configDirectory = System.IO.Path.Combine(tempDirectory.Path, "Config");
        Directory.CreateDirectory(configDirectory);
        string configurationPath = CreateJsonFile(configDirectory, "foundry.connect.config.json", bundle.ConfigurationJson);
        CreateBinaryFile(System.IO.Path.Combine(configDirectory, "Secrets"), "media-secrets.key", bundle.MediaSecretsKey);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfiguration configuration = service.Load();

        Assert.Equal("super-secret-passphrase", configuration.Wifi.Passphrase);
        Assert.Null(configuration.Wifi.PassphraseSecret);
    }

    [Fact]
    public void Load_WhenEncryptedPassphraseHasNoMediaKey_ThrowsConfigurationException()
    {
        using var environmentScope = new EnvironmentVariableScope("FOUNDRY_CONNECT_CONFIG", null);
        using var tempDirectory = new TemporaryDirectory();
        Foundry.Core.Models.Configuration.FoundryConnectProvisioningBundle bundle =
            new ConnectConfigurationGenerator().CreateProvisioningBundle(
                new CoreConfiguration.FoundryExpertConfigurationDocument
                {
                    Network = new CoreConfiguration.NetworkSettings
                    {
                        WifiProvisioned = true,
                        Wifi = new CoreConfiguration.WifiSettings
                        {
                            IsEnabled = true,
                            Ssid = "Corp WiFi",
                            SecurityType = "WPA2/WPA3-Personal",
                            Passphrase = "super-secret-passphrase"
                        }
                    }
                },
                tempDirectory.Path);
        string configDirectory = System.IO.Path.Combine(tempDirectory.Path, "Config");
        Directory.CreateDirectory(configDirectory);
        string configurationPath = CreateJsonFile(configDirectory, "foundry.connect.config.json", bundle.ConfigurationJson);

        var service = new ConnectConfigurationService(["--config", configurationPath], NullLogger<ConnectConfigurationService>.Instance);

        FoundryConnectConfigurationException exception = Assert.Throws<FoundryConnectConfigurationException>(service.Load);
        Assert.Contains("Media secret key file was not found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WhenEncryptedPassphraseUsesUnsupportedEnvelope_ThrowsConfigurationException()
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

        FoundryConnectConfigurationException exception = Assert.Throws<FoundryConnectConfigurationException>(service.Load);
        Assert.Contains("not supported", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-passphrase", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("unsupported", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WhenEncryptedPassphraseIsTampered_ThrowsConfigurationException()
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

        FoundryConnectConfigurationException exception = Assert.Throws<FoundryConnectConfigurationException>(service.Load);
        Assert.Contains("could not be decrypted", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-passphrase", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("AA", exception.ToString(), StringComparison.Ordinal);
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

        FoundryConnectConfiguration configuration = service.Load();

        Assert.Equal("legacy-passphrase", configuration.Wifi.Passphrase);
        Assert.Null(configuration.Wifi.PassphraseSecret);
    }

    private static string CreateJsonFile(string directoryPath, string fileName, string contents)
    {
        string filePath = System.IO.Path.Combine(directoryPath, fileName);
        using JsonDocument document = JsonDocument.Parse(contents);
        File.WriteAllText(filePath, document.RootElement.GetRawText());
        return filePath;
    }

    private static Foundry.Core.Models.Configuration.FoundryConnectProvisioningBundle CreatePersonalWifiProvisioningBundle(string stagingDirectoryPath)
    {
        return new ConnectConfigurationGenerator().CreateProvisioningBundle(
            new CoreConfiguration.FoundryExpertConfigurationDocument
            {
                Network = new CoreConfiguration.NetworkSettings
                {
                    WifiProvisioned = true,
                    Wifi = new CoreConfiguration.WifiSettings
                    {
                        IsEnabled = true,
                        Ssid = "Corp WiFi",
                        SecurityType = "WPA2/WPA3-Personal",
                        Passphrase = "super-secret-passphrase"
                    }
                }
            },
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
