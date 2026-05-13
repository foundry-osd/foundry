using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Services.Runtime;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Services.Configuration;

/// <summary>
/// Resolves, reads, decrypts, and normalizes Foundry.Connect runtime configuration.
/// </summary>
public sealed class ConnectConfigurationService : IConnectConfigurationService
{
    private const string DefaultConfigFileName = "foundry.connect.config.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly string[] _args;
    private readonly ILogger<ConnectConfigurationService> _logger;

    /// <summary>
    /// Initializes a configuration service.
    /// </summary>
    /// <param name="args">Command-line arguments used to resolve configuration.</param>
    /// <param name="logger">The logger used for configuration diagnostics.</param>
    public ConnectConfigurationService(string[] args, ILogger<ConnectConfigurationService> logger)
    {
        _args = args ?? Array.Empty<string>();
        _logger = logger;
    }

    /// <inheritdoc />
    public string? ConfigurationPath { get; private set; }

    /// <inheritdoc />
    public bool IsLoadedFromDisk { get; private set; }

    /// <inheritdoc />
    public FoundryConnectConfiguration Load()
    {
        ConfigurationResolution resolution = ResolveConfigurationPath(_args);
        ConfigurationPath = resolution.Path;
        if (string.IsNullOrWhiteSpace(ConfigurationPath))
        {
            IsLoadedFromDisk = false;
            _logger.LogInformation("No Foundry.Connect configuration file was resolved. Using built-in defaults.");
            return Normalize(new FoundryConnectConfiguration());
        }

        string fullPath = Path.GetFullPath(ConfigurationPath);
        if (!File.Exists(fullPath))
        {
            if (!resolution.IsRequired)
            {
                ConfigurationPath = null;
                IsLoadedFromDisk = false;
                _logger.LogInformation("Foundry.Connect configuration file was not found. Using built-in defaults. ConfigurationPath={ConfigurationPath}", fullPath);
                return Normalize(new FoundryConnectConfiguration());
            }

            throw new FoundryConnectConfigurationException($"Configuration file was not found: {fullPath}");
        }

        try
        {
            _logger.LogInformation("Loading Foundry.Connect configuration from disk. ConfigurationPath={ConfigurationPath}", fullPath);
            string json = File.ReadAllText(fullPath);
            FoundryConnectConfiguration? configuration = JsonSerializer.Deserialize<FoundryConnectConfiguration>(json, JsonOptions);
            if (configuration is null)
            {
                throw new FoundryConnectConfigurationException($"Configuration file is empty or invalid: {fullPath}");
            }

            configuration = DecryptEmbeddedSecrets(configuration, fullPath);
            ConfigurationPath = fullPath;
            IsLoadedFromDisk = true;
            _logger.LogInformation("Loaded Foundry.Connect configuration from disk. ConfigurationPath={ConfigurationPath}", fullPath);
            return Normalize(configuration);
        }
        catch (FoundryConnectConfigurationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FoundryConnectConfigurationException($"Configuration file could not be parsed: {fullPath}", ex);
        }
    }

    private static ConfigurationResolution ResolveConfigurationPath(IEnumerable<string> args)
    {
        string? envPath = Environment.GetEnvironmentVariable("FOUNDRY_CONNECT_CONFIG");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return new ConfigurationResolution(envPath, IsRequired: true);
        }

        string[] values = args.ToArray();
        for (int index = 0; index < values.Length; index++)
        {
            string value = values[index];
            if (value.Equals("--config", StringComparison.OrdinalIgnoreCase) && index + 1 < values.Length)
            {
                return new ConfigurationResolution(values[index + 1], IsRequired: true);
            }
        }

        return new ConfigurationResolution(
            ConnectWorkspacePaths.GetConfigFilePath(DefaultConfigFileName),
            IsRequired: ConnectWorkspacePaths.IsWinPeRuntime());
    }

    private static FoundryConnectConfiguration Normalize(FoundryConnectConfiguration configuration)
    {
        NetworkCapabilitiesOptions capabilities = configuration.Capabilities ?? new NetworkCapabilitiesOptions();
        InternetProbeOptions probe = configuration.InternetProbe ?? new InternetProbeOptions();

        string[] probeUris = probe.ProbeUris
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Where(static value => Uri.TryCreate(value, UriKind.Absolute, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (probeUris.Length == 0)
        {
            // Keep internet detection useful even when the generated configuration omits explicit probe endpoints.
            probeUris =
            [
                "http://www.msftconnecttest.com/connecttest.txt",
                "http://www.google.com"
            ];
        }

        return new FoundryConnectConfiguration
        {
            SchemaVersion = configuration.SchemaVersion <= 0
                ? FoundryConnectConfiguration.CurrentSchemaVersion
                : configuration.SchemaVersion,
            Capabilities = new NetworkCapabilitiesOptions
            {
                WifiProvisioned = capabilities.WifiProvisioned
            },
            Dot1x = configuration.Dot1x ?? new Dot1xSettings(),
            Wifi = NormalizeWifi(configuration.Wifi),
            InternetProbe = new InternetProbeOptions
            {
                ProbeUris = probeUris,
                TimeoutSeconds = Math.Clamp(probe.TimeoutSeconds, 1, 30)
            },
            Telemetry = configuration.Telemetry
        };
    }

    private static FoundryConnectConfiguration DecryptEmbeddedSecrets(
        FoundryConnectConfiguration configuration,
        string configurationPath)
    {
        WifiSettings? wifi = configuration.Wifi;
        if (wifi?.PassphraseSecret is null)
        {
            return configuration;
        }

        byte[] mediaSecretsKey = LoadMediaSecretsKey(configurationPath);
        string passphrase;
        try
        {
            passphrase = ConnectSecretEnvelopeProtector.Decrypt(wifi.PassphraseSecret, mediaSecretsKey);
        }
        finally
        {
            // The media key should not remain in memory after decrypting the runtime Wi-Fi passphrase.
            CryptographicOperations.ZeroMemory(mediaSecretsKey);
        }

        return new FoundryConnectConfiguration
        {
            SchemaVersion = configuration.SchemaVersion,
            Capabilities = configuration.Capabilities,
            Dot1x = configuration.Dot1x,
            Wifi = wifi with
            {
                Passphrase = passphrase,
                PassphraseSecret = null
            },
            InternetProbe = configuration.InternetProbe,
            Telemetry = configuration.Telemetry
        };
    }

    private static WifiSettings NormalizeWifi(WifiSettings? wifi)
    {
        return wifi is null
            ? new WifiSettings()
            : wifi with { PassphraseSecret = null };
    }

    private static byte[] LoadMediaSecretsKey(string configurationPath)
    {
        string? configurationDirectory = Path.GetDirectoryName(configurationPath);
        if (string.IsNullOrWhiteSpace(configurationDirectory))
        {
            throw new FoundryConnectConfigurationException("Configuration directory could not be resolved for encrypted secrets.");
        }

        string keyPath = Path.Combine(configurationDirectory, "Secrets", "media-secrets.key");
        if (!File.Exists(keyPath))
        {
            throw new FoundryConnectConfigurationException("Media secret key file was not found for encrypted secrets.");
        }

        return File.ReadAllBytes(keyPath);
    }

    private readonly record struct ConfigurationResolution(string? Path, bool IsRequired);
}
