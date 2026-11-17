// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text.Json;
using Foundry.Connect.Services.Runtime;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Runtime;
using Foundry.Telemetry;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Configuration;

/// <summary>
/// Resolves, reads and normalizes the Connect view of <c>foundry.winpe.config.json</c> and maps its network secrets.
/// </summary>
public sealed class ConnectConfigurationService : IConnectConfigurationService
{
    private const string WifiPassphraseSecretName = "wifi_passphrase";
    private const string WifiCertificatePasswordSecretName = "wifi_certificate_password";
    private const string WiredCertificatePasswordSecretName = "wired_certificate_password";
    private const string WiredClientCertificateSecretName = "wired_client_certificate";
    private const string WifiClientCertificateSecretName = "wifi_client_certificate";
    private const string WifiEnterpriseProfileSecretName = "wifi_enterprise_profile";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly ConfigurationResolution _resolution;
    private readonly ILogger<ConnectConfigurationService> _logger;
    private readonly BootMediaRuntimeContext _runtimeContext;

    /// <summary>
    /// Initializes a configuration service for the configuration of the booted media (see <see cref="ResolveConfiguration()"/>).
    /// </summary>
    /// <param name="logger">The logger used for configuration diagnostics.</param>
    public ConnectConfigurationService(ILogger<ConnectConfigurationService> logger)
        : this(ResolveConfiguration(), logger, CaptureRuntimeContext())
    {
    }

    /// <summary>Reads a configuration file that must exist, as the override does.</summary>
    internal ConnectConfigurationService(string configurationPath, ILogger<ConnectConfigurationService> logger)
        : this(configurationPath, logger, CaptureRuntimeContext())
    {
    }

    /// <summary>Uses captured runtime evidence so release-age evaluation is deterministic.</summary>
    internal ConnectConfigurationService(string configurationPath, ILogger<ConnectConfigurationService> logger, BootMediaRuntimeContext runtimeContext)
        : this(new ConfigurationResolution(configurationPath, IsRequired: true), logger, runtimeContext)
    {
    }

    private ConnectConfigurationService(ConfigurationResolution resolution, ILogger<ConnectConfigurationService> logger, BootMediaRuntimeContext runtimeContext)
    {
        _resolution = resolution;
        _logger = logger;
        _runtimeContext = runtimeContext;
    }

    private static BootMediaRuntimeContext CaptureRuntimeContext() =>
        BootMediaRuntimeContext.Capture(typeof(ConnectConfigurationService).Assembly.GetName().Version?.ToString());

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
            // No exception object and no path: every log level is mirrored remotely and both can carry the file name.
            Exception cause = exception.InnerException ?? exception;
            _logger.LogDebug(
                "Foundry.Connect configuration could not be loaded. FailureType={FailureType}, ErrorCode={ErrorCode}",
                cause.GetType().Name,
                cause.HResult);
            return ConnectConfigurationLoadResult.Failure(exception.Message, exception);
        }
    }

    private ConnectConfigurationLoadResult LoadCore()
    {
        ConfigurationPath = _resolution.Path;
        string fullPath = ResolveFullPath(ConfigurationPath);
        if (!File.Exists(fullPath))
        {
            if (!_resolution.IsRequired)
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
            FoundryConnectConfigurationDocument configuration = Normalize(ReadConnectView(json));
            EnforceSchemaPolicy(configuration.SchemaVersion, fullPath);

            (configuration, secrets) = ResolveSecrets(configuration, fullPath);
            bool isEligibleRuntime = _runtimeContext.IsEligible(ReadProvisioningSource(fullPath)) &&
                _runtimeContext.IsEligible(configuration.Telemetry.RuntimePayloadSource);
            BootMediaUpdateReason = BootMediaFreshnessPolicy.Evaluate(
                configuration.AuthoringVersion,
                _runtimeContext.RuntimeVersion,
                isEligibleRuntime);
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

    /// <summary>
    /// Resolves the configuration of the booted media: the <see cref="WinPeConfigurationPath.OverrideEnvironmentVariable"/>
    /// override, which must exist, otherwise <c>X:\Foundry\Config\foundry.winpe.config.json</c>, which must exist under WinPE.
    /// Outside WinPE a missing file loads the built-in defaults.
    /// </summary>
    internal static ConfigurationResolution ResolveConfiguration() =>
        ResolveConfiguration(WinPeConfigurationPath.Resolve(), ConnectWorkspacePaths.IsWinPeRuntime());

    internal static ConfigurationResolution ResolveConfiguration(WinPeConfigurationLocation location, bool isWinPeRuntime) =>
        new(location.Path, IsRequired: location.IsOverride || isWinPeRuntime);

    // The view keeps only the shared sections and the connect section, so a Deploy section never affects Connect startup.
    private static FoundryConnectConfigurationDocument ReadConnectView(string json) =>
        WinPeConfigurationReader.ReadModeView(json, WinPeConfigurationReader.ConnectSection).Deserialize<FoundryConnectConfigurationDocument>(JsonOptions)
        ?? throw new JsonException("The WinPE runtime configuration is empty.");

    private void EnforceSchemaPolicy(int schemaVersion, string configurationPath)
    {
        switch (WinPeConfigurationSchemaPolicy.Evaluate(schemaVersion))
        {
            case WinPeConfigurationSchemaDecision.Invalid:
                throw new FoundryConnectConfigurationException($"Configuration schema version {schemaVersion} is not valid: {configurationPath}");
            case WinPeConfigurationSchemaDecision.Newer:
                _logger.LogWarning(
                    "Foundry.Connect configuration was authored with a newer schema. Loading it anyway. SchemaVersion={SchemaVersion}, SupportedSchemaVersion={SupportedSchemaVersion}, ConfigurationPath={ConfigurationPath}",
                    schemaVersion,
                    ConfigurationSchemaVersions.WinPeCurrent,
                    configurationPath);
                break;
        }
    }

    private static FoundryConnectConfigurationDocument Normalize(FoundryConnectConfigurationDocument configuration)
    {
        return configuration with
        {
            Capabilities = configuration.Capabilities ?? new ConnectNetworkCapabilitiesSettings(),
            Network = configuration.Network ?? new WinPeNetworkSettings(),
            Dot1x = configuration.Dot1x ?? new ConnectDot1xSettings(),
            Wifi = configuration.Wifi ?? new ConnectWifiSettings(),
            // An explicit "telemetry": null in the file disables telemetry instead of failing startup: no consent, no data.
            Telemetry = configuration.Telemetry ?? TelemetrySettings.Disabled,
            Protection = configuration.Protection ?? new()
        };
    }

    /// <summary>
    /// Maps every provisioned network secret to its runtime holder. A deployment-keyed envelope stays
    /// <see cref="ConnectSecretStatus.Locked"/> until the deployment password unlocks it; any other envelope, or any envelope
    /// on media that cannot be unlocked (protection disabled or without a protected deployment key), is
    /// <see cref="ConnectSecretStatus.Unavailable"/>.
    /// </summary>
    /// <remarks>
    /// Envelopes come from the effective certificate sets (<see cref="ConnectCertificateSet"/>) and <c>wifi.passphraseSecret</c>.
    /// Encrypted files (client PFX, enterprise template) are read here as envelopes; plain files stay paths. The passphrase
    /// envelope leaves the configuration: only <see cref="ConnectRuntimeSecrets"/> holds it.
    /// </remarks>
    private (FoundryConnectConfigurationDocument Configuration, ConnectRuntimeSecrets Secrets) ResolveSecrets(
        FoundryConnectConfigurationDocument configuration,
        string configurationPath)
    {
        ConnectWifiSettings wifi = configuration.Wifi;
        ConnectCertificateSet wiredCertificates = ConnectCertificateSet.From(configuration.Dot1x);
        ConnectCertificateSet wifiCertificates = ConnectCertificateSet.From(wifi);
        configuration = configuration with { Wifi = wifi with { PassphraseSecret = null } };
        bool isUnlockable = configuration.Protection is { IsEnabled: true, ProtectedDeploymentKey: not null };
        var secrets = new ConnectRuntimeSecrets(
            ResolveSecret(wifi.PassphraseSecret, isUnlockable, WifiPassphraseSecretName),
            ResolveSecret(wifiCertificates.ClientCertificatePasswordSecret, isUnlockable, WifiCertificatePasswordSecretName),
            ResolveSecret(wiredCertificates.ClientCertificatePasswordSecret, isUnlockable, WiredCertificatePasswordSecretName),
            ResolveProtectedFile(wiredCertificates.IsClientCertificateContentProtected, wiredCertificates.ClientCertificatePath, configurationPath, isUnlockable, WiredClientCertificateSecretName),
            ResolveProtectedFile(wifiCertificates.IsClientCertificateContentProtected, wifiCertificates.ClientCertificatePath, configurationPath, isUnlockable, WifiClientCertificateSecretName),
            ResolveProtectedFile(wifi.IsEnterpriseProfileTemplateProtected, wifi.EnterpriseProfileTemplatePath, configurationPath, isUnlockable, WifiEnterpriseProfileSecretName),
            isUnlockable ? configuration.Protection : null);
        if (secrets.IsWiredLocked || secrets.IsWifiLocked)
        {
            _logger.LogInformation(
                "Protected network settings found. Wired={WiredLocked}, Wifi={WifiLocked}",
                secrets.IsWiredLocked,
                secrets.IsWifiLocked);
        }

        return (configuration, secrets);
    }

    private static bool IsDeploymentKeyed(SecretEnvelope? envelope) =>
        string.Equals(envelope?.KeyId, MediaSecretEnvelopeProtector.DeploymentKeyId, StringComparison.Ordinal);

    /// <summary>Maps one envelope; see <see cref="ResolveSecrets"/>.</summary>
    /// <param name="envelope">The envelope, or <see langword="null"/> when the media does not provision this secret.</param>
    /// <param name="isUnlockable">Whether the media can unwrap the deployment key from the deployment password.</param>
    /// <param name="secretName">The stable, non-identifying secret name used in the log.</param>
    private ConnectSecret ResolveSecret(SecretEnvelope? envelope, bool isUnlockable, string secretName)
    {
        if (envelope is null)
        {
            return ConnectSecret.NotProvisioned();
        }

        return IsDeploymentKeyed(envelope) && isUnlockable
            ? ConnectSecret.Locked(envelope)
            : Unavailable(secretName, ConnectSecretFailure.DecryptionFailed);
    }

    /// <summary>
    /// Reads a file staged as a deployment-keyed envelope and keeps it locked until the unlock. A file that is missing,
    /// larger than <see cref="ConnectProtectedFileLimits.MaxEnvelopeFileBytes"/> (the generator never stages one), not an envelope,
    /// or on media that cannot be unlocked is unavailable.
    /// </summary>
    /// <param name="isProtected">Whether the configuration marks the file as encrypted; a plain file stays a path and is not read here.</param>
    /// <param name="relativePath">The media-relative path from the configuration.</param>
    /// <param name="configurationPath">The configuration file path that anchors relative assets.</param>
    /// <param name="isUnlockable">Whether the media can unwrap the deployment key from the deployment password.</param>
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
            if (stream.Length > ConnectProtectedFileLimits.MaxEnvelopeFileBytes)
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

    /// <summary>A configuration path and whether a missing file is a configuration failure.</summary>
    internal readonly record struct ConfigurationResolution(string Path, bool IsRequired);

    private static string? ReadProvisioningSource(string configurationPath)
    {
        string markerPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configurationPath))!, WinPeWorkspaceLayout.ProvisioningSourceFileName);
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
