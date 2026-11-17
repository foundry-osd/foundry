// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text.Json;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Runtime;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Runtime;
using Microsoft.Extensions.Logging;
using BootMediaUpdateReason = Foundry.Core.Models.Configuration.BootMediaUpdateReason;
using ConfigurationSchemaVersions = Foundry.Core.Models.Configuration.ConfigurationSchemaVersions;

namespace Foundry.Deploy.Services.Configuration;

/// <summary>
/// Reads the Deploy view of <c>foundry.winpe.config.json</c>. A missing file is not a failure: Deploy then starts with its
/// built-in defaults.
/// </summary>
public sealed class DeployConfigurationService : IDeployConfigurationService
{
    private readonly ILogger<DeployConfigurationService> _logger;
    private readonly BootMediaRuntimeContext _runtimeContext;
    private readonly string _configurationPath;

    /// <summary>Reads the configuration of the booted media, or the <see cref="WinPeConfigurationPath.OverrideEnvironmentVariable"/> override.</summary>
    public DeployConfigurationService(ILogger<DeployConfigurationService> logger)
        : this(logger, WinPeConfigurationPath.Resolve().Path)
    {
    }

    internal DeployConfigurationService(ILogger<DeployConfigurationService> logger, string configurationPath)
        : this(logger, configurationPath, BootMediaRuntimeContext.Capture(typeof(DeployConfigurationService).Assembly.GetName().Version?.ToString()))
    {
    }

    /// <summary>Uses captured runtime evidence so release-age evaluation is deterministic.</summary>
    internal DeployConfigurationService(ILogger<DeployConfigurationService> logger, string configurationPath, BootMediaRuntimeContext runtimeContext)
    {
        _runtimeContext = runtimeContext;
        _logger = logger;
        _configurationPath = string.IsNullOrWhiteSpace(configurationPath)
            ? WinPeWorkspaceLayout.ConfigurationPath
            : configurationPath;
    }

    public DeployConfigurationLoadResult LoadOptional()
    {
        if (!File.Exists(_configurationPath))
        {
            _logger.LogInformation(
                "No deploy configuration was found at '{ConfigurationPath}'.",
                _configurationPath);

            return new DeployConfigurationLoadResult
            {
                ConfigurationPath = _configurationPath,
                Exists = false
            };
        }

        try
        {
            FoundryDeployConfigurationDocument? document = ReadDeployView(File.ReadAllText(_configurationPath));

            if (document is null)
            {
                const string failureMessage = "The configuration file was empty or could not be parsed.";
                _logger.LogWarning(
                    "Deploy configuration at '{ConfigurationPath}' could not be parsed: {FailureMessage}",
                    _configurationPath,
                    failureMessage);

                return new DeployConfigurationLoadResult
                {
                    ConfigurationPath = _configurationPath,
                    Exists = true,
                    FailureMessage = failureMessage
                };
            }

            if (WinPeConfigurationSchemaPolicy.Evaluate(document.SchemaVersion) == WinPeConfigurationSchemaDecision.Invalid)
            {
                throw new InvalidDataException($"The configuration schema version {document.SchemaVersion} is not valid.");
            }

            Foundry.Deploy.Services.Deployment.Unattend.UnattendCatalog.Validate(document.Unattend, document.Protection?.IsEnabled == true);
            ValidateDomainJoin(document);

            Foundry.Core.Services.Configuration.PreOobeConfigurationValidator.ThrowIfInvalid(new Foundry.Core.Models.Configuration.PreOobeSettings
            { IsEnabled = document.PreOobe.IsEnabled, Actions = document.PreOobe.Actions });

            if (WinPeConfigurationSchemaPolicy.Evaluate(document.SchemaVersion) == WinPeConfigurationSchemaDecision.Newer)
            {
                _logger.LogWarning(
                    "Deploy configuration at '{ConfigurationPath}' uses schema version {SchemaVersion}, newer than supported schema version {SupportedSchemaVersion}. Unknown properties will be ignored.",
                    _configurationPath,
                    document.SchemaVersion,
                    ConfigurationSchemaVersions.WinPeCurrent);
            }

            bool isEligibleRuntime = _runtimeContext.IsEligible(ReadProvisioningSource(_configurationPath)) &&
                _runtimeContext.IsEligible(document.Telemetry?.RuntimePayloadSource);
            BootMediaUpdateReason reason = BootMediaFreshnessPolicy.Evaluate(
                document.AuthoringVersion,
                _runtimeContext.RuntimeVersion,
                isEligibleRuntime);

            _logger.LogInformation(
                "Loaded deploy configuration from '{ConfigurationPath}' (SchemaVersion={SchemaVersion}).",
                _configurationPath,
                document.SchemaVersion);

            return new DeployConfigurationLoadResult
            {
                ConfigurationPath = _configurationPath,
                Exists = true,
                Document = document,
                BootMediaUpdateReason = reason,
                LatestReleaseCheck = BootMediaLatestReleaseCheck.Create(
                    document.AuthoringVersion,
                    _runtimeContext.RuntimeVersion,
                    isEligibleRuntime)
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(
                ex,
                "Failed to load deploy configuration from '{ConfigurationPath}'.",
                _configurationPath);

            return new DeployConfigurationLoadResult
            {
                ConfigurationPath = _configurationPath,
                Exists = true,
                FailureMessage = ex.Message,
                FailureException = ex
            };
        }
    }

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

    // The view keeps only the shared sections and the deploy section, so a Connect section never affects Deploy startup.
    private static FoundryDeployConfigurationDocument? ReadDeployView(string json) =>
        WinPeConfigurationReader.ReadModeView(json, WinPeConfigurationReader.DeploySection)
            .Deserialize<FoundryDeployConfigurationDocument>(ConfigurationJsonDefaults.SerializerOptions);

    private static void ValidateDomainJoin(FoundryDeployConfigurationDocument document)
    {
        var settings = document.DomainJoin;
        if (settings is null || settings.Domains is null || settings.Domains.Any(domain => domain?.OrganizationalUnits is null) ||
            document.Autopilot?.IsEnabled == true && settings.IsEnabled)
            throw new InvalidDataException("The domain join configuration is invalid.");
        bool interactive = settings.Mode == Foundry.Core.Models.Configuration.DomainJoinMode.Interactive;
        // Zero-touch media carries credentials for every domain; Interactive media carries none.
        HashSet<string> withCredentials = settings.Domains.Where(domain => domain.EncryptedCredentials is not null)
            .Select(domain => domain.AccountName ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!Foundry.Core.Services.Configuration.DomainJoinConfigurationValidator.EvaluateReadiness(
                Foundry.Deploy.Services.DomainJoin.DomainJoinPreparationService.ToAuthored(settings),
                withCredentials.Contains, document.Protection?.IsEnabled == true).IsValid ||
            settings.IsEnabled && !interactive && settings.Domains.Any(domain => domain.EncryptedCredentials is null) ||
            settings.IsEnabled && interactive && settings.Domains.Any(domain => domain.AccountName is not null || domain.EncryptedCredentials is not null))
            throw new InvalidDataException("The domain join configuration is invalid.");
    }
}
