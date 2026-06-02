using System.IO;
using System.Text.Json;
using Foundry.Deploy.Models.Configuration;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Configuration;

public sealed class DeployConfigurationService : IDeployConfigurationService
{
    public const string DefaultConfigurationPath = @"X:\Foundry\Config\foundry.deploy.config.json";

    private readonly ILogger<DeployConfigurationService> _logger;
    private readonly string _configurationPath;

    public DeployConfigurationService(ILogger<DeployConfigurationService> logger)
        : this(logger, DefaultConfigurationPath)
    {
    }

    internal DeployConfigurationService(ILogger<DeployConfigurationService> logger, string configurationPath)
    {
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

            if (document.SchemaVersion > FoundryDeployConfigurationDocument.CurrentSchemaVersion)
            {
                _logger.LogWarning(
                    "Deploy configuration at '{ConfigurationPath}' uses schema version {SchemaVersion}, newer than supported schema version {SupportedSchemaVersion}. Unknown properties will be ignored.",
                    _configurationPath,
                    document.SchemaVersion,
                    FoundryDeployConfigurationDocument.CurrentSchemaVersion);
            }

            bool isBootMediaUpdateRecommended = document.SchemaVersion < FoundryDeployConfigurationDocument.CurrentSchemaVersion;
            if (isBootMediaUpdateRecommended)
            {
                _logger.LogWarning(
                    "Deploy configuration at '{ConfigurationPath}' uses schema version {SchemaVersion}, older than current schema version {CurrentSchemaVersion}. Boot media update is recommended.",
                    _configurationPath,
                    document.SchemaVersion,
                    FoundryDeployConfigurationDocument.CurrentSchemaVersion);
            }

            _logger.LogInformation(
                "Loaded deploy configuration from '{ConfigurationPath}' (SchemaVersion={SchemaVersion}).",
                _configurationPath,
                document.SchemaVersion);

            return new DeployConfigurationLoadResult
            {
                ConfigurationPath = _configurationPath,
                Exists = true,
                Document = document,
                IsBootMediaUpdateRecommended = isBootMediaUpdateRecommended
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(
                ex,
                "Failed to load deploy configuration from '{ConfigurationPath}'.",
                _configurationPath);

            return new DeployConfigurationLoadResult
            {
                ConfigurationPath = _configurationPath,
                Exists = true,
                FailureMessage = ex.Message
            };
        }
    }
}
