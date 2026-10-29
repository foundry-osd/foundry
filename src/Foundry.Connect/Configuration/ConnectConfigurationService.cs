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
    private const string WiredClientCertificateSecretName = "wired_client_certificate";
    private const string WifiClientCertificateSecretName = "wifi_client_certificate";
    private const string WifiEnterpriseProfileSecretName = "wifi_enterprise_profile";

    // An encrypted PFX or WLAN template is small; a larger file is not read into memory.
    private const int MaxProtectedFileBytes = 4 * 1024 * 1024;

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
            // The passphrase envelope is consumed by ResolveSecrets; only the decrypted value or the locked envelope survives, in ConnectRuntimeSecrets.
            Wifi = (configuration.Wifi ?? new ConnectWifiSettings()) with { PassphraseSecret = null },
            InternetProbe = probe with
            {
                ProbeUris = probeUris,
                TimeoutSeconds = Math.Clamp(probe.TimeoutSeconds, 1, 30)
            }
        };
    }

    /// <summary>
    /// Maps every provisioned network secret to its runtime holder (L2-5, L4-9). The envelope <c>keyId</c> decides the key source:
    /// <c>media</c> is decrypted at once with the media key (schema 5); <c>deployment</c> stays <see cref="ConnectSecretStatus.Locked"/>
    /// until the deployment password unlocks it, or is <see cref="ConnectSecretStatus.Unavailable"/> on media without a protection block.
    /// </summary>
    /// <remarks>
    /// Envelopes come from the effective certificate sets (<see cref="ConnectCertificateSet"/>) and <c>wifi.passphraseSecret</c>.
    /// Encrypted files (client PFX, enterprise template) are read here as envelopes; plain files stay paths. The media key is read
    /// only when a <c>media</c> envelope exists, so schema 6 media without <c>Secrets\media-secrets.key</c> load normally.
    /// </remarks>
    private ConnectRuntimeSecrets ResolveSecrets(
        FoundryConnectConfigurationDocument configuration,
        JsonElement root,
        string configurationPath)
    {
        ConnectWifiSettings wifi = configuration.Wifi ?? new ConnectWifiSettings();
        ConnectCertificateSet wiredCertificates = ConnectCertificateSet.From(configuration.Dot1x ?? new ConnectDot1xSettings());
        ConnectCertificateSet wifiCertificates = ConnectCertificateSet.From(wifi);
        SecretEnvelope? wifiPassphraseSecret = wifi.PassphraseSecret;
        SecretEnvelope? wifiPasswordSecret = wifiCertificates.ClientCertificatePasswordSecret;
        SecretEnvelope? wiredPasswordSecret = wiredCertificates.ClientCertificatePasswordSecret;

        if (wifiPassphraseSecret is null && wifiPasswordSecret is null && wiredPasswordSecret is null &&
            !wiredCertificates.IsClientCertificateContentProtected &&
            !wifiCertificates.IsClientCertificateContentProtected &&
            !wifi.IsEnterpriseProfileTemplateProtected)
        {
            return new ConnectRuntimeSecrets(ReadLegacyWifiPassphrase(root), ConnectSecret.NotProvisioned(), ConnectSecret.NotProvisioned());
        }

        bool isUnlockable = configuration.Protection?.ProtectedDeploymentKey is not null;
        ConnectSecretFailure keyFailure = ConnectSecretFailure.None;
        byte[]? mediaSecretsKey = IsMediaKeyed(wifiPassphraseSecret) || IsMediaKeyed(wifiPasswordSecret) || IsMediaKeyed(wiredPasswordSecret)
            ? LoadMediaSecretsKey(configurationPath, out keyFailure)
            : null;
        var keys = new SecretKeys(mediaSecretsKey, keyFailure, isUnlockable);
        ConnectSecret? wifiPassphrase = null;
        ConnectSecret? wifiCertificatePassword = null;
        ConnectSecret? wiredCertificatePassword = null;
        try
        {
            // Built one at a time so a value already decrypted is zeroed if a later step throws.
            wifiPassphrase = wifiPassphraseSecret is null
                ? ReadLegacyWifiPassphrase(root)
                : ResolveSecret(wifiPassphraseSecret, keys, WifiPassphraseSecretName);
            wifiCertificatePassword = ResolveSecret(wifiPasswordSecret, keys, WifiCertificatePasswordSecretName);
            wiredCertificatePassword = ResolveSecret(wiredPasswordSecret, keys, WiredCertificatePasswordSecretName);
            var secrets = new ConnectRuntimeSecrets(
                wifiPassphrase,
                wifiCertificatePassword,
                wiredCertificatePassword,
                ResolveProtectedFile(wiredCertificates.IsClientCertificateContentProtected, wiredCertificates.ClientCertificatePath, configurationPath, isUnlockable, WiredClientCertificateSecretName),
                ResolveProtectedFile(wifiCertificates.IsClientCertificateContentProtected, wifiCertificates.ClientCertificatePath, configurationPath, isUnlockable, WifiClientCertificateSecretName),
                ResolveProtectedFile(wifi.IsEnterpriseProfileTemplateProtected, wifi.EnterpriseProfileTemplatePath, configurationPath, isUnlockable, WifiEnterpriseProfileSecretName),
                configuration.Protection);
            if (secrets.IsWiredLocked || secrets.IsWifiLocked)
            {
                _logger.LogInformation(
                    "Protected network settings found. Wired={WiredLocked}, Wifi={WifiLocked}",
                    secrets.IsWiredLocked,
                    secrets.IsWifiLocked);
            }

            return secrets;
        }
        catch
        {
            wifiPassphrase?.Clear();
            wifiCertificatePassword?.Clear();
            wiredCertificatePassword?.Clear();
            throw;
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

    private static bool IsMediaKeyed(SecretEnvelope? envelope) =>
        string.Equals(envelope?.KeyId, MediaSecretEnvelopeProtector.KeyId, StringComparison.Ordinal);

    private static bool IsDeploymentKeyed(SecretEnvelope? envelope) =>
        string.Equals(envelope?.KeyId, MediaSecretEnvelopeProtector.DeploymentKeyId, StringComparison.Ordinal);

    /// <summary>Maps one envelope by its key id; see <see cref="ResolveSecrets"/>.</summary>
    /// <param name="envelope">The envelope, or <see langword="null"/> when the media does not provision this secret.</param>
    /// <param name="keys">The key sources available to this load.</param>
    /// <param name="secretName">The stable, non-identifying secret name used in the log.</param>
    private ConnectSecret ResolveSecret(SecretEnvelope? envelope, SecretKeys keys, string secretName)
    {
        if (envelope is null)
        {
            return ConnectSecret.NotProvisioned();
        }

        if (IsMediaKeyed(envelope))
        {
            return DecryptSecret(envelope, keys.MediaSecretsKey, keys.MediaKeyFailure, secretName);
        }

        // A deployment-keyed envelope on media without a protection block can never be unlocked (D9).
        return IsDeploymentKeyed(envelope) && keys.IsUnlockable
            ? ConnectSecret.Locked(envelope)
            : Unavailable(secretName, ConnectSecretFailure.DecryptionFailed);
    }

    /// <summary>
    /// Decrypts one media-keyed envelope into a clearable buffer. A secret that cannot be read becomes <see cref="ConnectSecretStatus.Unavailable"/>
    /// instead of failing the load, so only the feature that needs it is disabled (D9, L2-5).
    /// </summary>
    /// <param name="envelope">The media-keyed envelope.</param>
    /// <param name="mediaSecretsKey">The media key, or <see langword="null"/> when the key file is missing or unreadable.</param>
    /// <param name="keyFailure">Why <paramref name="mediaSecretsKey"/> is <see langword="null"/>.</param>
    /// <param name="secretName">The stable, non-identifying secret name used in the log.</param>
    private ConnectSecret DecryptSecret(SecretEnvelope envelope, byte[]? mediaSecretsKey, ConnectSecretFailure keyFailure, string secretName)
    {
        if (mediaSecretsKey is null)
        {
            return Unavailable(secretName, keyFailure);
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

    /// <summary>
    /// Reads a file staged as a deployment-keyed envelope (L4-5, L4-6) and keeps it locked until the unlock. A file that is missing,
    /// larger than <see cref="MaxProtectedFileBytes"/>, not an envelope, or on media without a protection block is unavailable.
    /// </summary>
    /// <param name="isProtected">Whether the configuration marks the file as encrypted; a plain file stays a path and is not read here.</param>
    /// <param name="relativePath">The media-relative path from the configuration.</param>
    /// <param name="configurationPath">The configuration file path that anchors relative assets.</param>
    /// <param name="isUnlockable">Whether the media carries a protection block that can unwrap the deployment key.</param>
    /// <param name="secretName">The stable, non-identifying name used in the log; the file name is never logged.</param>
    private ConnectProtectedContent ResolveProtectedFile(bool isProtected, string? relativePath, string configurationPath, bool isUnlockable, string secretName)
    {
        if (!isProtected)
        {
            return ConnectProtectedContent.NotProvisioned();
        }

        SecretEnvelope? envelope = isUnlockable ? ReadEnvelopeFile(relativePath, configurationPath) : null;
        if (!IsDeploymentKeyed(envelope))
        {
            LogUnavailable(secretName, ConnectSecretFailure.DecryptionFailed);
            return ConnectProtectedContent.Unavailable(ConnectSecretFailure.DecryptionFailed);
        }

        return ConnectProtectedContent.Locked(envelope!);
    }

    // Returns null when the file is missing, unreadable, too large or not an envelope; the caller logs the stable secret name instead.
    private static SecretEnvelope? ReadEnvelopeFile(string? relativePath, string configurationPath)
    {
        try
        {
            string? path = ProvisionedWifiProfileResolver.ResolveAssetPath(relativePath, configurationPath);
            if (path is null)
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxProtectedFileBytes)
            {
                return null;
            }

            byte[] content = new byte[stream.Length];
            stream.ReadExactly(content);
            return JsonSerializer.Deserialize<SecretEnvelope>(content, JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private ConnectSecret Unavailable(string secretName, ConnectSecretFailure failure)
    {
        LogUnavailable(secretName, failure);
        return ConnectSecret.Unavailable(failure);
    }

    private void LogUnavailable(string secretName, ConnectSecretFailure failure) =>
        _logger.LogWarning(
            "Provisioned secret is unavailable. Secret={SecretName}, Failure={SecretFailure}",
            secretName,
            failure);

    /// <summary>The key sources of one load: the media key (schema 5) and whether the deployment key can be unlocked later.</summary>
    private readonly record struct SecretKeys(byte[]? MediaSecretsKey, ConnectSecretFailure MediaKeyFailure, bool IsUnlockable);

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

    // Returns null when the key file is missing or cannot be read; the secrets that need it become unavailable instead of failing the load.
    private static byte[]? LoadMediaSecretsKey(string configurationPath, out ConnectSecretFailure failure)
    {
        string? configurationDirectory = Path.GetDirectoryName(configurationPath);
        if (string.IsNullOrWhiteSpace(configurationDirectory))
        {
            throw new FoundryConnectConfigurationException("Configuration directory could not be resolved for encrypted secrets.");
        }

        string keyPath = Path.Combine(configurationDirectory, WinPeWorkspaceLayout.MediaSecretsKeyRelativePath);
        if (!File.Exists(keyPath))
        {
            failure = ConnectSecretFailure.MediaKeyMissing;
            return null;
        }

        try
        {
            failure = ConnectSecretFailure.None;
            return File.ReadAllBytes(keyPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failure = ConnectSecretFailure.MediaKeyUnreadable;
            return null;
        }
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
