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
    private const string WifiPassphraseSecretName = "wifi_passphrase";
    private const string WifiCertificatePasswordSecretName = "wifi_certificate_password";
    private const string WiredCertificatePasswordSecretName = "wired_certificate_password";

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
            _logger.LogDebug(exception, "Foundry.Connect configuration could not be loaded. ConfigurationPath={ConfigurationPath}", ConfigurationPath);
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
            return ConnectConfigurationLoadResult.Success(Normalize(new FoundryConnectConfigurationDocument()), ConnectRuntimeSecrets.None(), BootMediaUpdateReason);
        }

        string fullPath = ResolveFullPath(ConfigurationPath);
        if (!File.Exists(fullPath))
        {
            if (!resolution.IsRequired)
            {
                ConfigurationPath = null;
                IsLoadedFromDisk = false;
                _logger.LogInformation("Foundry.Connect configuration file was not found. Using built-in defaults. ConfigurationPath={ConfigurationPath}", fullPath);
                return ConnectConfigurationLoadResult.Success(Normalize(new FoundryConnectConfigurationDocument()), ConnectRuntimeSecrets.None(), BootMediaUpdateReason);
            }

            throw new FoundryConnectConfigurationException($"Configuration file was not found: {fullPath}");
        }

        ConnectRuntimeSecrets? secrets = null;
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

            secrets = ResolveSecrets(configuration, document.RootElement, fullPath);
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
            // A failed load hands no secret to anyone, so zero the buffers that were already decrypted.
            secrets?.ClearAll();
            throw;
        }
        catch (Exception ex)
        {
            secrets?.ClearAll();
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

    private ConnectRuntimeSecrets ResolveSecrets(
        FoundryConnectConfigurationDocument configuration,
        JsonElement root,
        string configurationPath)
    {
        ConnectWifiSettings? wifi = configuration.Wifi;
        ConnectDot1xSettings? dot1x = configuration.Dot1x;

        if (wifi?.PassphraseSecret is null &&
            wifi?.CertificatePfxPasswordSecret is null &&
            dot1x?.CertificatePfxPasswordSecret is null)
        {
            return new ConnectRuntimeSecrets(ReadLegacyWifiPassphrase(root), ConnectSecret.NotProvisioned(), ConnectSecret.NotProvisioned());
        }

        byte[]? mediaSecretsKey = LoadMediaSecretsKey(configurationPath);
        try
        {
            return new ConnectRuntimeSecrets(
                wifi?.PassphraseSecret is null
                    ? ReadLegacyWifiPassphrase(root)
                    : DecryptSecret(wifi.PassphraseSecret, mediaSecretsKey, WifiPassphraseSecretName),
                DecryptSecret(wifi?.CertificatePfxPasswordSecret, mediaSecretsKey, WifiCertificatePasswordSecretName),
                DecryptSecret(dot1x?.CertificatePfxPasswordSecret, mediaSecretsKey, WiredCertificatePasswordSecretName));
        }
        finally
        {
            // The media key should not remain in memory after decrypting the runtime network secrets.
            if (mediaSecretsKey is not null)
            {
                CryptographicOperations.ZeroMemory(mediaSecretsKey);
            }
        }
    }

    /// <summary>
    /// Decrypts one envelope into a clearable buffer. A secret that cannot be read becomes <see cref="ConnectSecretStatus.Unavailable"/>
    /// instead of failing the load, so only the feature that needs it is disabled (D9, L2-5).
    /// </summary>
    /// <param name="envelope">The envelope, or <see langword="null"/> when the media does not provision this secret.</param>
    /// <param name="mediaSecretsKey">The media key, or <see langword="null"/> when the key file is missing.</param>
    /// <param name="secretName">The stable, non-identifying secret name used in the log.</param>
    private ConnectSecret DecryptSecret(SecretEnvelope? envelope, byte[]? mediaSecretsKey, string secretName)
    {
        if (envelope is null)
        {
            return ConnectSecret.NotProvisioned();
        }

        if (mediaSecretsKey is null)
        {
            return Unavailable(secretName, ConnectSecretFailure.MediaKeyMissing);
        }

        try
        {
            return ConnectSecret.Available(MediaSecretEnvelopeProtector.DecryptChars(envelope, mediaSecretsKey, [MediaSecretEnvelopeProtector.KeyId]));
        }
        catch (CryptographicException)
        {
            // Unsupported envelope, tampering or invalid base64url. The exception is not logged: the value must stay out of logs.
            return Unavailable(secretName, ConnectSecretFailure.DecryptionFailed);
        }
        catch (ArgumentException)
        {
            // The key length is the only argument the protector rejects here.
            return Unavailable(secretName, ConnectSecretFailure.MediaKeyInvalid);
        }
    }

    private ConnectSecret Unavailable(string secretName, ConnectSecretFailure failure)
    {
        _logger.LogWarning(
            "Provisioned secret is unavailable. Secret={SecretName}, Failure={SecretFailure}",
            secretName,
            failure);
        return ConnectSecret.Unavailable(failure);
    }

    // Older media carry a plaintext wifi.passphrase that the Core runtime record no longer models.
    private static ConnectSecret ReadLegacyWifiPassphrase(JsonElement root)
    {
        if (!TryGetPropertyIgnoreCase(root, LegacyWifiPropertyName, out JsonElement wifi) ||
            !TryGetPropertyIgnoreCase(wifi, LegacyWifiPassphrasePropertyName, out JsonElement passphrase) ||
            passphrase.GetString() is not { } value)
        {
            return ConnectSecret.NotProvisioned();
        }

        return ConnectSecret.Available(value.ToCharArray());
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

    // Returns null when the key file is missing; the secrets that need it become unavailable instead of failing the load.
    private static byte[]? LoadMediaSecretsKey(string configurationPath)
    {
        string? configurationDirectory = Path.GetDirectoryName(configurationPath);
        if (string.IsNullOrWhiteSpace(configurationDirectory))
        {
            throw new FoundryConnectConfigurationException("Configuration directory could not be resolved for encrypted secrets.");
        }

        string keyPath = Path.Combine(configurationDirectory, WinPeWorkspaceLayout.MediaSecretsKeyRelativePath);
        return File.Exists(keyPath) ? File.ReadAllBytes(keyPath) : null;
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
