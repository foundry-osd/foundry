// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Connect.Services.Runtime;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Runtime;
using Foundry.Telemetry;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Configuration;

/// <summary>
/// Resolves, reads, decrypts, and normalizes Foundry.Connect runtime configuration.
/// </summary>
public sealed class ConnectConfigurationService : IConnectConfigurationService
{
    private const string DefaultConfigFileName = "foundry.connect.config.json";
    private const string LegacyWifiPropertyName = "wifi";
    private const string LegacyWifiPassphrasePropertyName = "passphrase";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonDocumentOptions JsonDocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip
    };

    private readonly string[] _args;
    private readonly ILogger<ConnectConfigurationService> _logger;
    private readonly BootMediaRuntimeContext _runtimeContext;

    /// <summary>
    /// Initializes a configuration service.
    /// </summary>
    /// <param name="args">Command-line arguments used to resolve configuration.</param>
    /// <param name="logger">The logger used for configuration diagnostics.</param>
    public ConnectConfigurationService(string[] args, ILogger<ConnectConfigurationService> logger)
        : this(args, logger, BootMediaRuntimeContext.Capture(typeof(ConnectConfigurationService).Assembly.GetName().Version?.ToString()))
    {
    }

    /// <summary>Uses captured runtime evidence so release-age evaluation is deterministic.</summary>
    internal ConnectConfigurationService(string[] args, ILogger<ConnectConfigurationService> logger, BootMediaRuntimeContext runtimeContext)
    {
        _runtimeContext = runtimeContext;
        _args = args ?? Array.Empty<string>();
        _logger = logger;
    }

    /// <inheritdoc />
    public string? ConfigurationPath { get; private set; }

    /// <inheritdoc />
    public bool IsLoadedFromDisk { get; private set; }

    /// <inheritdoc />
    public bool IsBootMediaUpdateRecommended => BootMediaUpdateReason != BootMediaUpdateReason.None;

    /// <inheritdoc />
    public BootMediaUpdateReason BootMediaUpdateReason { get; private set; }

    /// <inheritdoc />
    public ConnectConfigurationLoadResult Load()
    {
        BootMediaUpdateReason = BootMediaUpdateReason.None;
        try
        {
            return LoadCore();
        }
        catch (FoundryConnectConfigurationException exception)
        {
            _logger.LogError(exception, "Foundry.Connect configuration could not be loaded. ConfigurationPath={ConfigurationPath}", ConfigurationPath);
            return ConnectConfigurationLoadResult.Failure(exception.Message, exception);
        }
    }

    private ConnectConfigurationLoadResult LoadCore()
    {
        ConfigurationResolution resolution = ResolveConfigurationPath(_args);
        ConfigurationPath = resolution.Path;
        if (string.IsNullOrWhiteSpace(ConfigurationPath))
        {
            IsLoadedFromDisk = false;
            _logger.LogInformation("No Foundry.Connect configuration file was resolved. Using built-in defaults.");
            return ConnectConfigurationLoadResult.Success(Normalize(new FoundryConnectConfigurationDocument()), new ConnectRuntimeSecrets(), BootMediaUpdateReason);
        }

        string fullPath = ResolveFullPath(ConfigurationPath);
        if (!File.Exists(fullPath))
        {
            if (!resolution.IsRequired)
            {
                ConfigurationPath = null;
                IsLoadedFromDisk = false;
                _logger.LogInformation("Foundry.Connect configuration file was not found. Using built-in defaults. ConfigurationPath={ConfigurationPath}", fullPath);
                return ConnectConfigurationLoadResult.Success(Normalize(new FoundryConnectConfigurationDocument()), new ConnectRuntimeSecrets(), BootMediaUpdateReason);
            }

            throw new FoundryConnectConfigurationException($"Configuration file was not found: {fullPath}");
        }

        try
        {
            _logger.LogInformation("Loading Foundry.Connect configuration from disk. ConfigurationPath={ConfigurationPath}", fullPath);
            string json = File.ReadAllText(fullPath);
            using JsonDocument document = JsonDocument.Parse(json, JsonDocumentOptions);
            FoundryConnectConfigurationDocument? configuration = document.Deserialize<FoundryConnectConfigurationDocument>(JsonOptions);
            if (configuration is null)
            {
                throw new FoundryConnectConfigurationException($"Configuration file is empty or invalid: {fullPath}");
            }

            EnforceSchemaPolicy(configuration.SchemaVersion, fullPath);

            ConnectRuntimeSecrets secrets = ResolveSecrets(configuration, document.RootElement, fullPath);
            configuration = Normalize(configuration);
            BootMediaUpdateReason = BootMediaFreshnessPolicy.Evaluate(
                configuration.AuthoringVersion,
                _runtimeContext.RuntimeVersion,
                _runtimeContext.IsEligible(ReadProvisioningSource(fullPath)) &&
                _runtimeContext.IsEligible(configuration.Telemetry.RuntimePayloadSource));
            ConfigurationPath = fullPath;
            IsLoadedFromDisk = true;
            _logger.LogInformation("Loaded Foundry.Connect configuration from disk. ConfigurationPath={ConfigurationPath}", fullPath);
            return ConnectConfigurationLoadResult.Success(configuration, secrets, BootMediaUpdateReason);
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

    /// <summary>Turns a malformed configuration path into a configuration failure instead of an escaping exception.</summary>
    private static string ResolveFullPath(string configurationPath)
    {
        try
        {
            return Path.GetFullPath(configurationPath);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            throw new FoundryConnectConfigurationException($"Configuration path is not valid: {configurationPath}", ex);
        }
    }

    internal static ConfigurationResolution ResolveConfigurationPath(IEnumerable<string> args)
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

    private void EnforceSchemaPolicy(int schemaVersion, string configurationPath)
    {
        switch (ConnectConfigurationSchemaPolicy.Evaluate(schemaVersion))
        {
            case ConnectSchemaDecision.Invalid:
                throw new FoundryConnectConfigurationException($"Configuration schema version {schemaVersion} is not valid: {configurationPath}");
            case ConnectSchemaDecision.Newer:
                _logger.LogWarning(
                    "Foundry.Connect configuration was authored with a newer schema. Loading it anyway. SchemaVersion={SchemaVersion}, SupportedSchemaVersion={SupportedSchemaVersion}, ConfigurationPath={ConfigurationPath}",
                    schemaVersion,
                    FoundryConnectConfigurationDocument.CurrentSchemaVersion,
                    configurationPath);
                break;
        }
    }

    private static FoundryConnectConfigurationDocument Normalize(FoundryConnectConfigurationDocument configuration)
    {
        ConnectInternetProbeSettings probe = configuration.InternetProbe ?? new ConnectInternetProbeSettings();

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

        return configuration with
        {
            Capabilities = configuration.Capabilities ?? new ConnectNetworkCapabilitiesSettings(),
            Network = configuration.Network ?? new ConnectNetworkSettings(),
            Dot1x = configuration.Dot1x ?? new ConnectDot1xSettings(),
            // An explicit "telemetry": null in the file falls back to the defaults instead of failing startup.
            Telemetry = configuration.Telemetry ?? new TelemetrySettings(),
            // The passphrase envelope is consumed by ResolveSecrets; only the decrypted value survives, in ConnectRuntimeSecrets.
            Wifi = (configuration.Wifi ?? new ConnectWifiSettings()) with { PassphraseSecret = null },
            InternetProbe = probe with
            {
                ProbeUris = probeUris,
                TimeoutSeconds = Math.Clamp(probe.TimeoutSeconds, 1, 30)
            }
        };
    }

    private static ConnectRuntimeSecrets ResolveSecrets(
        FoundryConnectConfigurationDocument configuration,
        JsonElement root,
        string configurationPath)
    {
        ConnectWifiSettings? wifi = configuration.Wifi;
        ConnectDot1xSettings? dot1x = configuration.Dot1x;

        // Older media carry a plaintext wifi.passphrase that the Core runtime record no longer models.
        string? legacyPassphrase = wifi?.PassphraseSecret is null
            ? ReadLegacyWifiPassphrase(root)
            : null;

        if (wifi?.PassphraseSecret is null &&
            wifi?.CertificatePfxPasswordSecret is null &&
            dot1x?.CertificatePfxPasswordSecret is null)
        {
            return new ConnectRuntimeSecrets { WifiPassphrase = legacyPassphrase };
        }

        byte[] mediaSecretsKey = LoadMediaSecretsKey(configurationPath);
        try
        {
            return new ConnectRuntimeSecrets
            {
                WifiPassphrase = wifi?.PassphraseSecret is null
                    ? legacyPassphrase
                    : DecryptSecret(wifi.PassphraseSecret, mediaSecretsKey),
                WifiCertificatePfxPassword = wifi?.CertificatePfxPasswordSecret is null
                    ? null
                    : DecryptSecret(wifi.CertificatePfxPasswordSecret, mediaSecretsKey),
                WiredCertificatePfxPassword = dot1x?.CertificatePfxPasswordSecret is null
                    ? null
                    : DecryptSecret(dot1x.CertificatePfxPasswordSecret, mediaSecretsKey)
            };
        }
        finally
        {
            // The media key should not remain in memory after decrypting the runtime network secrets.
            CryptographicOperations.ZeroMemory(mediaSecretsKey);
        }
    }

    private static string DecryptSecret(SecretEnvelope envelope, byte[] mediaSecretsKey)
    {
        try
        {
            return MediaSecretEnvelopeProtector.DecryptString(envelope, mediaSecretsKey);
        }
        catch (CryptographicException ex)
        {
            // Core messages carry no secret material and are the user-facing Connect texts.
            throw new FoundryConnectConfigurationException(ex.Message, ex);
        }
        catch (ArgumentException ex)
        {
            // The key length is the only argument the protector rejects here; map by type because Core words it differently.
            throw new FoundryConnectConfigurationException("Media secret key has an invalid length.", ex);
        }
    }

    private static string? ReadLegacyWifiPassphrase(JsonElement root)
    {
        if (!TryGetPropertyIgnoreCase(root, LegacyWifiPropertyName, out JsonElement wifi) ||
            !TryGetPropertyIgnoreCase(wifi, LegacyWifiPassphrasePropertyName, out JsonElement passphrase))
        {
            return null;
        }

        return passphrase.GetString();
    }

    // Matches property names the same way the case-insensitive deserializer options do.
    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static byte[] LoadMediaSecretsKey(string configurationPath)
    {
        string? configurationDirectory = Path.GetDirectoryName(configurationPath);
        if (string.IsNullOrWhiteSpace(configurationDirectory))
        {
            throw new FoundryConnectConfigurationException("Configuration directory could not be resolved for encrypted secrets.");
        }

        string keyPath = Path.Combine(configurationDirectory, WinPeWorkspaceLayout.MediaSecretsKeyRelativePath);
        if (!File.Exists(keyPath))
        {
            throw new FoundryConnectConfigurationException("Media secret key file was not found for encrypted secrets.");
        }

        return File.ReadAllBytes(keyPath);
    }

    internal readonly record struct ConfigurationResolution(string? Path, bool IsRequired);

    private static string? ReadProvisioningSource(string configurationPath)
    {
        string markerPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configurationPath))!, "foundry.connect.provisioning-source.txt");
        try
        {
            return File.ReadAllText(markerPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
