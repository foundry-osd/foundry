// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text.Json;
using Foundry.Core.Services.Runtime;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Runtime;
using Microsoft.Extensions.Logging;
using BootMediaUpdateReason = Foundry.Core.Models.Configuration.BootMediaUpdateReason;
using ConfigurationSchemaVersions = Foundry.Core.Models.Configuration.ConfigurationSchemaVersions;

namespace Foundry.Deploy.Services.Configuration;

public sealed class DeployConfigurationService : IDeployConfigurationService
{
    public const string DefaultConfigurationPath = @"X:\Foundry\Config\foundry.deploy.config.json";

    private readonly ILogger<DeployConfigurationService> _logger;
    private readonly BootMediaRuntimeContext _runtimeContext;
    private readonly string _configurationPath;

    public DeployConfigurationService(ILogger<DeployConfigurationService> logger)
        : this(logger, DefaultConfigurationPath)
    {
    }

    internal DeployConfigurationService(ILogger<DeployConfigurationService> logger, string configurationPath)
        : this(logger, configurationPath, BootMediaRuntimeContext.Capture())
    {
    }

    /// <summary>Uses captured runtime evidence so release-age evaluation is deterministic.</summary>
    internal DeployConfigurationService(ILogger<DeployConfigurationService> logger, string configurationPath, BootMediaRuntimeContext runtimeContext)
    {
        _runtimeContext = runtimeContext;
        _logger = logger;
        _configurationPath = string.IsNullOrWhiteSpace(configurationPath)
            ? DefaultConfigurationPath
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
            using FileStream stream = File.OpenRead(_configurationPath);
            FoundryDeployConfigurationDocument? document = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(
                stream,
                ConfigurationJsonDefaults.SerializerOptions);

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

            Foundry.Deploy.Services.Deployment.Unattend.UnattendCatalog.Validate(document.Unattend, document.Protection?.IsEnabled == true);
            ValidateDomainJoin(document);

            document = DeployConfigurationMigration.ApplySchemaMigrations(document);
            Foundry.Core.Services.Configuration.PreOobeConfigurationValidator.ThrowIfInvalid(new Foundry.Core.Models.Configuration.PreOobeSettings
            { IsEnabled = document.PreOobe.IsEnabled, Actions = document.PreOobe.Actions });

            if (document.SchemaVersion > ConfigurationSchemaVersions.DeployCurrent)
            {
                _logger.LogWarning(
                    "Deploy configuration at '{ConfigurationPath}' uses schema version {SchemaVersion}, newer than supported schema version {SupportedSchemaVersion}. Unknown properties will be ignored.",
                    _configurationPath,
                    document.SchemaVersion,
                    ConfigurationSchemaVersions.DeployCurrent);
            }

            BootMediaUpdateReason reason = BootMediaFreshnessPolicy.Evaluate(
                document.AuthoringVersion,
                _runtimeContext.RuntimeVersion,
                _runtimeContext.IsEligible(ReadProvisioningSource(_configurationPath)) &&
                _runtimeContext.IsEligible(document.Telemetry?.RuntimePayloadSource));

            _logger.LogInformation(
                "Loaded deploy configuration from '{ConfigurationPath}' (SchemaVersion={SchemaVersion}).",
                _configurationPath,
                document.SchemaVersion);

            return new DeployConfigurationLoadResult
            {
                ConfigurationPath = _configurationPath,
                Exists = true,
                Document = document,
                BootMediaUpdateReason = reason
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
        string markerPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configurationPath))!, "foundry.deploy.provisioning-source.txt");
        try
        {
            return File.ReadAllText(markerPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void ValidateDomainJoin(FoundryDeployConfigurationDocument document)
    {
        var settings = document.DomainJoin;
        if (settings is null || settings.OrganizationalUnits is null ||
            document.Autopilot?.IsEnabled == true && settings.IsEnabled ||
            !Foundry.Core.Services.Configuration.DomainJoinConfigurationValidator.EvaluateReadiness(
                Foundry.Deploy.Services.DomainJoin.DomainJoinPreparationService.ToAuthored(settings),
                settings.EncryptedCredentials is not null, document.Protection?.IsEnabled == true).IsValid ||
            settings.IsEnabled && settings.Mode == Foundry.Core.Models.Configuration.DomainJoinMode.Interactive &&
                (settings.AccountName is not null || settings.EncryptedCredentials is not null))
            throw new InvalidDataException("The domain join configuration is invalid.");
    }
}
